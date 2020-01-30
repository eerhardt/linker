using System;
using System.Collections.Generic;
using Mono.Cecil;
using System.IO;
using System.Linq;
using System.Text;
using System.Diagnostics;

namespace Mono.Linker.Analysis
{

	public enum Grouping
	{
		None,
		Caller,
		Callee,
	}

	public struct FormattedStacktrace
	{
		public string asString;
		public List<string> asList;
		public string asJson;
		public List<MethodDefinition> asMethods;
	}

	public class Formatter
	{
		bool firstStacktrace = true;
		public void WriteStacktrace (AnalyzedStacktrace st)
		{
			if (json) {
				if (!firstStacktrace) {
					textWriter.WriteLine (",");
				} else {
					// TODO: when grouping stacktraces,
					// output group separators.
					firstStacktrace = false;
				}
				textWriter.WriteLine (st.stacktrace.asJson);
			} else {
				textWriter.WriteLine (st.stacktrace.asString);
			}
		}

		public void WriteGroupedStacktraces (IOrderedEnumerable<KeyValuePair<MethodDefinition, HashSet<AnalyzedStacktrace>>> stacktracesPerGroup)
		{
			if (json) {
				textWriter.WriteLine ("{");
			}
			bool first = true;
			foreach (var e in stacktracesPerGroup) {
				var group = e.Key;
				var stacktraces = e.Value;
				if (json) {
					if (first)
						first = false;
					else
						textWriter.WriteLine (",");
					textWriter.WriteLine ("\"" + FormatMethod(group) + "\": [");
				} else {
					textWriter.WriteLine ("---");
					textWriter.WriteLine ("--- stacktraces for group: " + FormatMethod(group));
					textWriter.WriteLine ("---");
				}
				firstStacktrace = true;
				foreach (var st in stacktraces) {
					WriteStacktrace (st);
				}
				if (json) {
					textWriter.Write ("]");
				}
			}
			if (json) {
				textWriter.WriteLine ();
				textWriter.WriteLine ("}");
			}
		}

		TextWriter textWriter;
		ApiFilter apiFilter;
		ICallGraph<MethodDefinition> callGraph;
		IntMapping<MethodDefinition> mapping;

		bool json = false;

		bool usingStringInput = true;

		public Formatter (CallGraph callGraph,
						 IntMapping<MethodDefinition> mapping,
						 bool json = false,
						 TextWriter textWriter = null)
		{
			this.callGraph = callGraph;
			this.mapping = mapping;
			this.apiFilter = callGraph.apiFilter;
			this.json = json;
			if (textWriter == null) {
				textWriter = Console.Out;
			}
			this.textWriter = textWriter;
		}

		public static string FormatMethod (MethodReference m)
		{
			StringBuilder sb = new StringBuilder ();
			sb.Append (m.DeclaringType.FullName);
			sb.Append ("::");
			sb.Append (m.Name);
			sb.Append ("(");
			var ps = m.Parameters;
			if (ps != null && ps.Count > 0) {
				sb.Append (ps [0].ParameterType);
				sb.Append (" ");
				sb.Append (ps [0].Name);
				for (int i = 1; i < ps.Count; i++) {
					sb.Append (", ");
					sb.Append (ps [i].ParameterType);
					sb.Append (" ");
					sb.Append (ps [i].Name);
				}
			}
			sb.Append (") -> ");
			sb.Append (m.ReturnType);
			return sb.ToString ();
		}

		public void PrintEdge ((MethodDefinition caller, MethodDefinition callee) e)
		{
			textWriter.WriteLine (Formatter.FormatMethod (e.caller));
			textWriter.WriteLine (" -> " + Formatter.FormatMethod (e.callee));
		}


		static string Prefix (int i)
		{
			//return String.Format("{0,-6}", i) + ": ";
			// return i.ToString("D6") + ": ";
			return "";
		}

		public FormattedStacktrace FormatStacktrace (IntBFSResult r, int destination = -1, bool reverse = false)
		{
			if (destination == -1) {
				Debug.Assert (r.destinations.Count == 1);
				destination = r.destinations.Single ();
			}
			if (destination != -1) {
				Debug.Assert (r.destinations.Contains (destination));
			}
			int i = destination; // this would be the interesting method normally.
								 // however in my case, it's the public or virtual API.
			var stacktrace = new List<string> ();
			var output = new List<string> ();
			var methods = new List<MethodDefinition> ();
			MethodDefinition methodDef;
			string prefix = Prefix (i);
			if (usingStringInput) {
				methodDef = mapping.intToMethod [i];
				// should never be null, because we already skip nulls when determining entry points.
				// yet somehow we get null...
				// TODO: investigate this.
				// Debug.Assert(methodDef != null);
				if (!reverse) {
					if (methodDef == null) {
						output.Add (prefix + "---------- (???)");
					} else {
						output.Add (prefix + "---------- (" + apiFilter.GetApiAnnotation (methodDef).ToString () + ")");
					}
				}

				methods.Add (methodDef);
				output.Add (prefix + methodDef.ToString ());
				stacktrace.Add (methodDef.ToString ());
			} else {
				// methodDef = intToMethodDef[i];
				// if (!reverse) {
				//     output.Add(prefix + "---------- (" + apiFilter.GetInterestingReason(methodDef).ToString() + ")");
				// }
				// methodString = FormatMethod(methodDef);
				// output.Add(prefix + methodString);
				// stacktrace.Add(methodString);
			}
			while (r.prev [i] != i) {
				i = r.prev [i];
				prefix = Prefix (i);
				if (usingStringInput) {
					methodDef = mapping.intToMethod [i];
					// this may give back a null methoddef. not sure why exactly.
					if (methodDef == null) {
						// TODO: investigate. for now, don't use FormatMethod.
						// Console.WriteLine("resolution failure!");
					}
					methods.Add (methodDef);
					output.Add (prefix + methodDef.ToString ());
					stacktrace.Add (methodDef.ToString ());
				} else {
					// methodDef = intToMethodDef[i];
					// var method = methodDef;
					// methodString = FormatMethod(method);
					// output.Add(prefix + methodString);
					// stacktrace.Add(methodString);
				}
			}
			if (reverse) {
				prefix = Prefix (i);
				if (usingStringInput) {
					methodDef = mapping.intToMethod [i];
					if (methodDef == null) {
						output.Add (prefix + "---------- (???)");
					} else {
						output.Add (prefix + "---------- (" + apiFilter.GetApiAnnotation (methodDef).ToString () + ")");
					}
				} else {
					// methodDef = intToMethodDef[i];
					// output.Add(prefix + "---------- (" + apiFilter.GetInterestingReason(methodDef).ToString() + ")");
				}
			}
			Debug.Assert (i == r.source);
			if (reverse) {
				stacktrace.Reverse ();
				output.Reverse ();
				methods.Reverse ();
			}

			var sb = new StringBuilder ();
			foreach (var o in output) {
				sb.AppendLine (o);
			}

			string asString = null, asJson = null;
			if (json) {
				asJson = $"[{Environment.NewLine}    {string.Join ("," + Environment.NewLine + "    ", output.Select (s => "\"" + s + "\""))}{Environment.NewLine}]";
			} else {
				asString = sb.ToString ();
			}

			return new FormattedStacktrace {
				asString = asString,
				asList = stacktrace,
				asJson = asJson,
				asMethods = methods
			};
		}

	}
}
