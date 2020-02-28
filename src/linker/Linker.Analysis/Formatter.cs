using Mono.Cecil;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;

namespace Mono.Linker.Analysis
{

	public enum Grouping
	{
		None,
		Caller,
		ImmediatedCaller,
		Callee,
	}

	public struct ReportedStacktrace
	{
		public List<MethodDefinition> Methods;
	}

	public class Formatter
	{
		bool firstStacktrace = true;
		public void WriteStacktrace (AnalyzedStacktrace st, string indent = "")
		{
			if (json) {
				if (!firstStacktrace) {
					textWriter.WriteLine (",");
				} else {
					// TODO: when grouping stacktraces,
					// output group separators.
					firstStacktrace = false;
				}
				WriteAsJson (st, textWriter, indent);
			} else {
				WriteAsString (st, textWriter, indent);
			}
		}

		private struct ImmediateCallerKey
		{
			public string AssemblyName { get; set; }
			public string CallerCallee { get; set; }
			public string Category { get; set; }

			public ImmediateCallerKey (AnalyzedStacktrace stackTrace)
			{
				var caller = stackTrace.stacktrace.Methods.Skip (1).First ();
				var callee = stackTrace.stacktrace.Methods.First ();

				AssemblyName = caller.Module.Assembly.Name.Name;
				CallerCallee = TypeChecker.GetMethodFullNameWithSignature (caller) + " depends on "
					+ TypeChecker.GetMethodFullNameWithSignature (callee);
				Category = stackTrace.annotation.Category;
			}
		}

		public void WriteImmediateCallerStacktraces (IEnumerable<AnalyzedStacktrace> stacktraces)
		{
			var assemblyGroups = stacktraces
				.GroupBy (s => new ImmediateCallerKey (s))
				.GroupBy (g => (g.Key.AssemblyName, g.Key.Category))
				.GroupBy (g => g.Key.AssemblyName);

			if (json) {
				textWriter.WriteLine ("{");
			}
			bool first = true;
			foreach (var assemblyGroup in assemblyGroups.OrderBy (g => g.Key)) {
				if (json) {
					if (first)
						first = false;
					else
						textWriter.WriteLine (",");

					textWriter.WriteLine ("\"" + assemblyGroup.Key + "\": {");
				} else {
					textWriter.WriteLine ("###");
					textWriter.WriteLine ("### assembly: " + assemblyGroup.Key);
					textWriter.WriteLine ("###");
				}

				bool firstCategory = true;
				foreach (var categoryGroup in assemblyGroup.OrderByDescending (g => g.SelectMany (v => v).Count ())) {

					if (json) {
						if (firstCategory)
							firstCategory = false;
						else
							textWriter.WriteLine (",");

						textWriter.WriteLine ("    \"" + categoryGroup.Key.Category + "\": {");
					} else {
						textWriter.WriteLine ("@@@");
						textWriter.WriteLine ("@@@ category: " + assemblyGroup.Key);
						textWriter.WriteLine ("@@@");
					}

					bool firstMethod = true;
					foreach (var methodGroup in categoryGroup.OrderByDescending (g => g.Count ())) {
						if (json) {
							if (firstMethod)
								firstMethod = false;
							else
								textWriter.WriteLine (",");

							textWriter.WriteLine ("        \"" + methodGroup.First ().annotation.Category + " - " + methodGroup.Key.CallerCallee + "\": [");
						} else {
							textWriter.WriteLine ("---");
							textWriter.WriteLine ("--- " + methodGroup.Key.CallerCallee);
							textWriter.WriteLine ("---");
						}

						firstStacktrace = true;
						foreach (var st in methodGroup) {
							WriteStacktrace (st, indent: "            ");
						}

						if (json) {
							textWriter.Write ("]");
						}
					}

					if (json) {
						textWriter.Write ("}");
					}
				}

				if (json) {
					textWriter.Write ("}");
				}
			}
			if (json) {
				textWriter.WriteLine ();
				textWriter.WriteLine ("}");
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
					textWriter.WriteLine ("\"" + TypeChecker.GetMethodFullNameWithSignature (group) + "\": [");
				} else {
					textWriter.WriteLine ("---");
					textWriter.WriteLine ("--- stacktraces for group: " + TypeChecker.GetMethodFullNameWithSignature (group));
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

		readonly TextWriter textWriter;
		readonly IntMapping<MethodDefinition> mapping;
		readonly bool json = false;

		public Formatter (IntMapping<MethodDefinition> mapping, bool json = false, TextWriter textWriter = null)
		{
			this.mapping = mapping;
			this.json = json;
			if (textWriter == null) {
				textWriter = Console.Out;
			}
			this.textWriter = textWriter;
		}

		public void PrintEdge ((MethodDefinition caller, MethodDefinition callee) e)
		{
			textWriter.WriteLine (TypeChecker.GetMethodFullNameWithSignature (e.caller));
			textWriter.WriteLine (" -> " + TypeChecker.GetMethodFullNameWithSignature (e.callee));
		}


		public static void WriteAsString (AnalyzedStacktrace analyzedStacktrace, TextWriter writer, string indent = "")
		{
			writer.Write (indent);
			writer.Write ($"---------- ({analyzedStacktrace.annotation})");
			foreach (var frameMethod in analyzedStacktrace.stacktrace.Methods) {
				writer.Write (indent);
				writer.Write(TypeChecker.GetMethodFullNameWithSignature (frameMethod));
			}
		}

		public static void WriteAsJson (AnalyzedStacktrace analyzedStacktrace, TextWriter writer, string indent = "")
		{
			writer.Write (indent);
			writer.WriteLine ("[");
			writer.Write (indent);
			writer.Write ($"\"---------- ({analyzedStacktrace.annotation})\"");
			foreach (var frameMethod in analyzedStacktrace.stacktrace.Methods) {
				writer.WriteLine (",");
				writer.Write (indent);
				writer.Write ("\"");
				writer.Write (TypeChecker.GetMethodFullNameWithSignature (frameMethod));
				writer.Write ("\"");
			}
			writer.WriteLine ();
			writer.Write (indent);
			writer.Write ("]");
		}

		public ReportedStacktrace FormatStacktrace (IntBFSResult r, int destination = -1)
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
			var methods = new List<MethodDefinition> ();
			MethodDefinition methodDef;
			methodDef = mapping.intToMethod [i];
			// should never be null, because we already skip nulls when determining entry points.
			// yet somehow we get null...
			// TODO: investigate this.
			// Debug.Assert(methodDef != null);

			methods.Add (methodDef);

			while (r.prev [i] != i) {
				i = r.prev [i];
				methodDef = mapping.intToMethod [i];
				// this may give back a null methoddef. not sure why exactly.
				if (methodDef == null) {
					// TODO: investigate. for now, don't use FormatMethod.
					// Console.WriteLine("resolution failure!");
					continue;
				}
				methods.Add (methodDef);
			}

			Debug.Assert (i == r.source);
			methods.Reverse ();

			return new ReportedStacktrace {
				Methods = methods
			};
		}

	}
}
