using Mono.Cecil;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Mono.Linker.Analysis
{
	public class Analyzer
	{
		public Dictionary<string, HashSet<TypeDefinition>> hitTypesPerNS;
		public List<AnalyzedStacktrace> analyzedStacktraces;
		public Dictionary<string, HashSet<AnalyzedStacktrace>> stacktracesPerCategory;
		public Dictionary<MethodDefinition, HashSet<AnalyzedStacktrace>> stacktracesPerGroup;
		public List<AnalyzedStacktrace> allStacktraces;

		private readonly ICallGraph<MethodDefinition> callGraph;
		private readonly ApiFilter apiFilter;
		private readonly Dictionary<MethodDefinition, HashSet<MethodDefinition>> resolvedReflectionCalls;
		private readonly IntCallGraph intCallGraph;
		private readonly IntMapping<MethodDefinition> mapping;
		private readonly Formatter formatter;
		private readonly Grouping grouping;
		private readonly AnnotationStore annotationStore;

		private bool [] isVirtualMethod;
		private bool [] isAnnotatedSafeMethod;
		private ApiAnnotation [] interestingReasons;
		private int numInterestingMethods;
		private int numEntryMethods;


		// track methods for each interesting reason
		public Dictionary<string, HashSet<MethodDefinition>> methodsPerCategory;

		void TrackCategory (ApiAnnotation annotation, MethodDefinition method)
		{
			if (methodsPerCategory == null) {
				methodsPerCategory = new Dictionary<string, HashSet<MethodDefinition>> ();
			}

			if (!methodsPerCategory.TryGetValue (annotation.Category, out HashSet<MethodDefinition> methods)) {
				methods = new HashSet<MethodDefinition> ();
				methodsPerCategory [annotation.Category] = methods;
			}
			methods.Add (method);
		}

		public Analyzer (CallGraph callGraph,
						IntCallGraph intCallGraph,
						IntMapping<MethodDefinition> mapping,
						ApiFilter apiFilter,
						Dictionary<MethodDefinition, HashSet<MethodDefinition>> resolvedReflectionCalls,
						AnnotationStore annotationStore,
						Formatter formatter = null,
						Grouping grouping = Grouping.None)
		{
			this.callGraph = callGraph;
			this.mapping = mapping;
			this.intCallGraph = intCallGraph;
			this.apiFilter = apiFilter;
			this.resolvedReflectionCalls = resolvedReflectionCalls;
			this.annotationStore = annotationStore;
			this.formatter = formatter;
			this.grouping = grouping;
		}

		void ReportMethodReasons ()
		{
			if (methodsPerCategory is null)
				return;

			var sortedReasons = methodsPerCategory.OrderByDescending (e => e.Value.Count);
			Console.WriteLine ("summary: found " + numInterestingMethods + " interesting methods");
			foreach (var e in sortedReasons) {
				var reason = e.Key;
				var methods = e.Value;
				Console.WriteLine (methods.Count + " methods are " + reason);
			}
			var allReasons = (InterestingReasonKind [])Enum.GetValues (typeof (InterestingReasonKind));
			foreach (var reason in allReasons) {
				if (reason == InterestingReasonKind.None) {
					continue;
				}
				if (methodsPerCategory.ContainsKey (reason.ToString())) {
					var methods = methodsPerCategory [reason.ToString()];
					Debug.Assert (methods != null && methods.Count > 0);
				} else {
					Console.WriteLine ("0 methods are " + reason);
				}
			}
		}

		// for each interesting reason, give a count, and a few samples.
		void ReportBuckets ()
		{
			if (stacktracesPerCategory == null) {
				return;
			}

			var sortedReasons = stacktracesPerCategory.OrderByDescending (e => e.Value.Count);

			// give a sample of some stacktraces for each category. disabled.
			// int count = 10;
			// foreach (var e in sortedReasons) {
			//     var reason = e.Key;
			//     var sts = e.Value;
			//     Console.WriteLine(sts.Count + " stacktraces are " + reason + ":");
			//     foreach (var st in sts) {
			//         if (count == 0) {
			//             break;
			//         }
			//         Program.PrintStacktrace(st.stacktrace);
			//         Console.WriteLine();
			//         count--;
			//     }
			// }
			Console.WriteLine ("summary: found " + stacktrace_count + " stacktraces");
			foreach (var e in sortedReasons) {
				var reason = e.Key;
				var sts = e.Value;
				Console.WriteLine (sts.Count + " stacktraces are " + reason);
			}
			var allReasons = (InterestingReasonKind [])Enum.GetValues (typeof (InterestingReasonKind));
			foreach (var reason in allReasons) {
				if (reason == InterestingReasonKind.None) {
					continue;
				}
				if (stacktracesPerCategory.ContainsKey (reason.ToString ())) {
					var sts = stacktracesPerCategory [reason.ToString ()];
					Debug.Assert (sts != null && sts.Count > 0);
				} else {
					Console.WriteLine ("0 stacktraces are " + reason);
				}
			}
		}

		// HashSet<List<string>>[] buckets;
		// void ReportStacktraceBuckets(AnalyzedStacktrace res) {
		//     int reportCount = 3;
		//     string prefix = "  ";
		//     var c = (int)res.category;
		//     buckets[c].Add(res.stacktrace);
		//     if (buckets[c].Count == reportCount) {
		//         Console.WriteLine("-----------------------------");
		//         Console.WriteLine("a few examples of stack traces that are " + res.category);
		//         foreach (var stacktrace in buckets[c]) {
		// 
		//             string methodString;
		//             MethodDefinition methodDef;
		//             if (usingStringInput) {
		//                 methodString = res.stacktrace[0];
		//                 methodDef = GetCecilMethod(methodString);
		//                 if (methodDef == null) {
		//                     Console.WriteLine(prefix + "---------- (???)");
		//                 } else {
		//                     Console.WriteLine(prefix + "---------- (" + GetInterestingReason(methodDef).ToString() + ")");
		//                 }
		//             } else {
		//                 throw new Exception("not supported");
		//             }
		// 
		//             foreach (var m in stacktrace) {
		//                 Console.WriteLine(prefix + m);
		//             }
		//         }
		//     }
		// }

		public void Analyze ()
		{
			numInterestingMethods = 0;

			interestingReasons = new ApiAnnotation [intCallGraph.numMethods];
			isVirtualMethod = new bool [callGraph.Methods.Count];
			isAnnotatedSafeMethod = new bool [callGraph.Methods.Count];
			bool[] isPublicOrVirtual = new bool [callGraph.Methods.Count];
			int [] [] safeEdges = new int [callGraph.Methods.Count] [];
			var virtualMethodsWithWarnAnnotation = new Queue<(MethodDefinition, int, WarnApiAnnotation)> ();
			for (int i = 0; i < intCallGraph.numMethods; i++) {
				var cecilMethod = mapping.intToMethod [i];
				if (cecilMethod == null) {
					continue;
				}

				if (intCallGraph.isInteresting [i]) {
					numInterestingMethods++;
					var annotation = apiFilter.GetApiAnnotation (cecilMethod);
					interestingReasons [i] = annotation;
					TrackCategory (annotation, cecilMethod);

					if (annotation is WarnApiAnnotation warnAnnotation && cecilMethod.IsVirtual) {
						virtualMethodsWithWarnAnnotation.Enqueue ((cecilMethod, i, warnAnnotation));
					}
				}
				if (intCallGraph.isEntry [i]) {
					numEntryMethods++;
				}
				if (cecilMethod.IsVirtual) {
					isVirtualMethod [i] = true;
				}
				if (intCallGraph.isEntry [i] || isVirtualMethod [i]) {
					isPublicOrVirtual [i] = true;
				}
				if (apiFilter.IsAnnotatedLinkerFriendlyApi (cecilMethod)) {
					isAnnotatedSafeMethod [i] = true;
				}
				if (resolvedReflectionCalls.TryGetValue(cecilMethod, out HashSet<MethodDefinition> callees)) {
					int [] safeCallees = new int [callees.Count];
					int j = 0;
					foreach (MethodDefinition calleeMethodDef in callees) {
						int calleeMethodIndex = mapping.methodToInt [calleeMethodDef];
						safeCallees [j++] = calleeMethodIndex;
					}

					safeEdges [i] = safeCallees;
				}
			}

			// This implies annotations from base methods to overrides.
			// This is effectively a workaround for a reporting issue - ideally we would not report callstacks
			// for cases where an override has a (override) dependency on a base method which is marked with a warn annotation.
			// But currently we don't have enough information in the graph to determine this.
			// Note that it is technically correct to mark all overrides with the same annotation as the base
			// since there's no way for the linker to tell if the base method is ever used or not.
			// Side note: Technically if the base method is abstract it will never be used, but we do mark some abstract methods
			// as dangerous basically as a way to propagate issues from the internal overrides to publicly facing APIs.
			// For example Type.GetMethod itself is perfectly safe since it's abstract, it's the RuntimeType.GetMethod which is dangerous
			// all code is calling through Type.GetMethod, so we would either have to blame the RuntimeType.ctor (which is hard as there's no managed
			// callsite for it and it would make lot of noise) or we mark the base method as dangerous.
			while (virtualMethodsWithWarnAnnotation.Count > 0) {
				(var methodDefinition, var methodIndex, var warnAnnotation) = virtualMethodsWithWarnAnnotation.Dequeue ();
				var overrides = annotationStore.GetOverrides (methodDefinition);
				if (overrides == null)
					continue;

				foreach (var overrideMethod in overrides) {
					var overrideMethodDefinition = overrideMethod.Override;
					if (!mapping.methodToInt.TryGetValue (overrideMethodDefinition, out var overrideMethodIndex))
						continue;

					var existingAnnotation = interestingReasons [overrideMethodIndex];
					if (existingAnnotation == null || existingAnnotation is LinkerUnanalyzedAnnotation) {
						if (existingAnnotation != null) {
							Console.WriteLine ($"Overwriting annotation {existingAnnotation}");
						}

						// Console.WriteLine ($"Adding implied annotation {warnAnnotation} to {overrideMethodDefinition} because it overrides annotated {methodDefinition}");
						interestingReasons [overrideMethodIndex] = warnAnnotation;
						intCallGraph.isInteresting [overrideMethodIndex] = true;

						// Propagate recuresively
						virtualMethodsWithWarnAnnotation.Enqueue ((overrideMethodDefinition, overrideMethodIndex, warnAnnotation));
					} else {
						Console.WriteLine ($"### {overrideMethodDefinition}{Environment.NewLine}  Skipping application of implied annotation since it already has one.{Environment.NewLine}   {warnAnnotation}{Environment.NewLine}   {methodDefinition}");
					}
				}
			}

			Console.WriteLine ("found " + intCallGraph.numMethods + " methods");
			Console.WriteLine ("found " + numEntryMethods + " entry methods");
			Console.WriteLine ("found " + numInterestingMethods + " \"interesting\" methods");

			Console.WriteLine ("built call graph...");

			hitTypesPerNS = new Dictionary<string, HashSet<TypeDefinition>> ();
			// concurrently print out call stacks as we find them
			var cq = new ConcurrentQueue<AnalyzedStacktrace> ();

			var buckets = new Dictionary<string, int> ();
			Action reportAction = () => {
				// int numCategories = (int)Category.NumCategories;
				// Debug.Assert(numCategories == Enum.GetNames(typeof(Category)).Length - 1);
				// buckets = new HashSet<List<string>>[numCategories];
				// int i;
				// for (i = 0; i < numCategories; i++) {
				//     buckets[i] = new HashSet<List<string>>();
				// }

				while (true) {
					if (cq.TryDequeue (out AnalyzedStacktrace res)) {
						if (res.source == -2) {
							// signals completion
							break;
						}
						RecordStacktrace (res);
						// ReportStacktraceBuckets(res);
						if (!buckets.ContainsKey (res.category)) {
							buckets [res.category] = 1;
						} else {
							buckets [res.category]++;
						}

						// var c = (int)res.category;

						// if ((completion_counter % 100) == 0) {
						//     Console.WriteLine(completion_counter + "/" + numMethods);
						// }
					}
				}
				WriteRecordedStacktraces ();
				// for (i = 0; i < numCategories; i++) {
				//     Console.WriteLine(buckets[i].Count + " stack traces are " + (Category)i);
				// }
			};
			var reportTask = Task.Run (reportAction);

			int num_stacktraces = 0;


			IntBFS.AllPairsBFS (
				neighbors: intCallGraph.callers,                 // search bottom-up (callees to callers).
				isSource: intCallGraph.isInteresting,            // look for a shortest path from each "interesting" method...
				isDestination: isPublicOrVirtual,                // ...to each public or virtual method.
				numMethods: intCallGraph.numMethods,
				excludePathsToSources: true,                     // ...that don't go through any other "interesting" methods.
				ignoreEdgesTo: isAnnotatedSafeMethod,            // ignore calls from annotated safe methods (edges to safe methods in the bottom-up case)
				ignoreEdgesFrom: isVirtualMethod,                // ignore calls to virtual methods (edges from virtual methods in the bottom-up case)
				ignoreEdges: safeEdges,                          // ignore all edges already marked as safe

				// don't report paths that go through another public or virtual method
				//   this is ensured by the BFS algorithm - it won't consider edges from a destination node. (calls to a public/virtual)
				// also don't report paths that go through another interesting method
				//   this is ensured by the isSource check.

				resultAction: (IntBFSResult r) => {
					var publicOrVirtuals = r.destinations;
					var sourceInterestingMethod = r.source;
					if (publicOrVirtuals.Count > 0) { // if a public or virtual method was reachable from a reflection method...
													  // only print the first one for now.
						foreach (var destination in r.destinations) {
							ReportedStacktrace f;
							if (formatter != null) {
								f = formatter.FormatStacktrace (r, destination: destination);
							} else {
								throw new Exception ("unable to format!");
							}

							var annotation = interestingReasons [sourceInterestingMethod];
							if (annotation is LinkerUnanalyzedAnnotation linkerUnanalyzedAnnotation) {
								foreach (var reflectionCalls in linkerUnanalyzedAnnotation.UnanalyzedReflectionCalls) {
									ReportedStacktrace reportedStacktrace = new ReportedStacktrace {
										Methods = f.Methods.Prepend (reflectionCalls.ReflectionMethod).ToList ()
									};

									cq.Enqueue (new AnalyzedStacktrace {
										source = sourceInterestingMethod,
										stacktrace = reportedStacktrace,
										category = CategorizeStacktraceWithCecil (reportedStacktrace.Methods),
										annotation = new WarnApiAnnotation {
											Aspect = annotation.Aspect,
											Category = annotation.Category,
											Message = reflectionCalls.Message
										}
									}); ;
								}
							} else {
								// queue it for printing
								cq.Enqueue (new AnalyzedStacktrace {
									source = sourceInterestingMethod,
									stacktrace = f,
									category = CategorizeStacktraceWithCecil (f.Methods),
									annotation = annotation
								});
								Interlocked.Add (ref num_stacktraces, 1);
							}
						}
					}
				});
			cq.Enqueue (new AnalyzedStacktrace { source = -2 }); // signal completion

			// record the shortest reverse path from each "dangerous" API to each public or virtual method
			//var result = Parallel.For(0, numInterestingMethods, (iInteresting, state) =>
			//{

			// non-virtual call graph.
			// all shortest non-loopy paths from public/virtual (that isn't itself "dangerous") to first "dangerous" API
			// want: all ways that public surface area reaches dangerous methods.
			// when it reaches one dangerous method, don't need to continue to find all dangerous callees of the dangerous method
			// but DO need to show paths to different dangerous methods that don't overlay
			// and if there are multiple paths to same dangerous method?
			//   only need to report one. but if one of the paths goes through a different dangerous method, need to report both.
			//   never report cycles.
			//
			// scan from "interesting" methods to callers.
			// stop when we see a method that is public or virtual
			// what if we see an "interesting" method?
			// don't report lone "interesting" methods that are public APIs - want at least one caller.
			// ignore virtual calls, and stop at public APIs or virtual methods.
			// don't consider calls from a "safe" method
			// don't consider calls from an "unsafe" method (since we don't want these to show up as extra paths with the unsafe API in the middle)
			// for a bottom-up search, starting with the dangerous APIs, find callers.

			// record the shortest path from each source to an interesting method
			//    var result = Parallel.For(0, numEntryMethods, (iEntry, state) =>
			//    // for (int iEntry = 0; iEntry < numEntryMethods; iEntry++)
			//    {
			//        int source = entryMethods[iEntry];
			//        var r = IntBFS(source, neighborsList, isInterestingMethod);
			//    
			//        int interestingMethod = r.interestingMethod;
			//        int i = interestingMethod;
			//        // string prefix = String.Format("{0,-6}", source) + ": ";
			//        if (i != -1) { // if an interesting method was reachable from source...
			//            // format a call stack
			//            var f = FormatCallStack(r);
			//            stacktraces[source] = f.asList;
			//            // queue the stacktrace for printing
			//            var res = new AnalyzedStacktrace {
			//                stacktrace = f.stacktrace,
			//                formattedStacktrace = f.asString,
			//                category = CategorizeStacktrace(f.stacktrace)
			//            };
			//            cq.Enqueue(res);
			//            Interlocked.Add(ref num_stacktraces, 1);
			//        }
			//        Interlocked.Add(ref completion_counter, 1);
			//    });
			// }
			reportTask.Wait ();
			Console.WriteLine ("-------------");
			Console.WriteLine ("found " + num_stacktraces + " stack traces");
			var items = new List<(string, int)> ();
			foreach (var item in buckets) {
				var category = item.Key;
				var hitCount = item.Value;
				items.Add ((category, hitCount));
			}
			items.Sort ((x, y) => ((int)x.Item2).CompareTo ((int)y.Item2));
			items.Reverse ();
			foreach (var item in items) {
				var (category, hitCount) = item;
				Console.WriteLine (hitCount + " stack traces are " + category);
			}
			//foreach (var ns in CecilAdapter.namespaces) {
			//    if (!buckets.ContainsKey(ns)) {
			//        Console.WriteLine(0 + " stack traces are " + ns);
			//    }
			//}
			Console.WriteLine ("----------");
			ReportBuckets ();
			Console.WriteLine ("----------");
			ReportTypeCounts ();
			Console.WriteLine ("----------");
			ReportMethodReasons ();
			// ReportRandomSample();
		}


		public int stacktrace_count = 0;

		public MethodDefinition Group (AnalyzedStacktrace st)
		{
			Debug.Assert (grouping != Grouping.None);
			switch (grouping) {
				case Grouping.Callee:
					return st.stacktrace.Methods.First ();
				case Grouping.ImmediatedCaller:
					return st.stacktrace.Methods.Skip (1).First ();
				case Grouping.Caller:
					return st.stacktrace.Methods.Last ();
				default:
					throw new Exception ("unsupported grouping!");
			}
		}

		public void RecordStacktrace (AnalyzedStacktrace st)
		{
			if (st.annotation is SuppressApiAnnotation)
				return;

			stacktrace_count++;

			if (allStacktraces == null) {
				allStacktraces = new List<AnalyzedStacktrace> ();
			}
			allStacktraces.Add (st);

			if (grouping != Grouping.None) {
				// bucketize based on group
				if (stacktracesPerGroup == null) {
					stacktracesPerGroup = new Dictionary<MethodDefinition, HashSet<AnalyzedStacktrace>> ();
				}
				var group = Group (st);
				if (!stacktracesPerGroup.TryGetValue (group, out HashSet<AnalyzedStacktrace> sts)) {
					sts = new HashSet<AnalyzedStacktrace> ();
					stacktracesPerGroup [group] = sts;
				}
				sts.Add (st);
			}

			// bucketize based on interesting reason
			{
				if (stacktracesPerCategory == null) {
					stacktracesPerCategory = new Dictionary<string, HashSet<AnalyzedStacktrace>> ();
				}
				if (!stacktracesPerCategory.TryGetValue (st.annotation.Category, out HashSet<AnalyzedStacktrace> sts)) {
					sts = new HashSet<AnalyzedStacktrace> ();
					stacktracesPerCategory [st.annotation.Category] = sts;
				}
				sts.Add (st);
			}


			var method = st.stacktrace.Methods.Last ();
			if (method == null) {
				Console.WriteLine ("TODO!");
				return;
			}

			if (analyzedStacktraces == null) {
				analyzedStacktraces = new List<AnalyzedStacktrace> ();
			}
			analyzedStacktraces.Add (st);

			// find outermost declaring type? no. count nested types.
			var type = method.DeclaringType;
			var nsType = type;
			while (nsType.DeclaringType != null) {
				nsType = nsType.DeclaringType;
			}
			var ns = nsType.Namespace;
			if (String.IsNullOrEmpty (ns)) {
				return;
			}
			if (!hitTypesPerNS.TryGetValue (ns, out HashSet<TypeDefinition> types)) {
				types = new HashSet<TypeDefinition> ();
				hitTypesPerNS [ns] = types;
			}
			types.Add (type);
		}

		public void WriteRecordedStacktraces ()
		{
			if (formatter == null)
				return;
			if (grouping == Grouping.None) {
				foreach (var st in allStacktraces) {
					formatter.WriteStacktrace (st);
				}
			} else if (grouping == Grouping.ImmediatedCaller) {
				formatter.WriteImmediateCallerStacktraces (allStacktraces);
			} else {
				var ordered = stacktracesPerGroup.OrderByDescending (e => e.Value.Count);
				formatter.WriteGroupedStacktraces (ordered);
			}
		}

		public void ReportRandomSample ()
		{
			int sample = 100;
			int total = analyzedStacktraces.Count;
			var random = new Random ();
			// without replacement.
			var hs = new HashSet<int> ();
			for (int i = 0; i < sample; i++) {
				while (true) {
					var r = random.Next (0, total);
					if (hs.Add (r)) {
						break;
					}
				}
			}
			random.Next (0, total);

			Console.WriteLine ("A random sample of " + sample + " stacktraces:");
			foreach (var r in hs) {
				var analyzedStacktrace = analyzedStacktraces [r];
				Formatter.WriteAsString (analyzedStacktrace, Console.Out);
			}
		}

		public void ReportTypeCounts ()
		{
			var ordered = hitTypesPerNS.OrderByDescending (e => e.Value.Count);

			HashSet<TypeDefinition> allTypes = callGraph.Methods.Select (m => m.DeclaringType).ToHashSet ();
			HashSet<string> allNamespaces = allTypes.Select (t => t.Namespace).Where (ns => !string.IsNullOrEmpty (ns)).ToHashSet ();

			var totalTypesPerNS = new Dictionary<string, int> ();
			foreach (var t in allTypes) {
				var type = t;
				while (type.DeclaringType != null) {
					type = type.DeclaringType;
				}
				var ns = type.Namespace;
				if (!String.IsNullOrEmpty (ns)) {
					if (!totalTypesPerNS.ContainsKey (ns)) {
						totalTypesPerNS [ns] = 1;
					} else {
						totalTypesPerNS [ns]++;
					}
				}
			}

			// output the actual data
			foreach (var e in ordered) {
				var ns = e.Key;
				var types = e.Value;
				Console.WriteLine (ns + ": " + types.Count + " / " + totalTypesPerNS [ns]);
			}

			// output namespaces with zero hits
			var zeroNamespaces = allNamespaces.Where (ns => !hitTypesPerNS.ContainsKey (ns)).OrderBy (ns => ns);
			foreach (var ns in zeroNamespaces) {
				Console.WriteLine (ns + ": 0 / " + totalTypesPerNS [ns]);
			}
		}


		// pass in a specific destination to print,
		// otherwise it's assumed the result only has a single destination.



		// not used
		public enum Category
		{
			AnonymousGetHashCode,
			ObjectToStringMoveNext,
			ObjectEqualsDynamicBinding,
			ResourceManagerGetStringMoveNext,
			MulticastDelegateGetMethodBase,
			XmlSchemaMoveNext,
			ResourceEnumeratorDeserializeObject,
			IEnumeratorMoveNextReflection,
			ComponentModelEqualityComparer,
			PublicReflectionApi,
			DefaultEqualityComparer,
			NetEventSourceFormatTypeName,
			ExceptionToStringMemberInfo,
			Uncategorized,
			NumCategories
		}


		public string CategorizeStacktraceWithCecil (List<MethodDefinition> stacktrace)
		{
			var sourceMethod = stacktrace.Last ();
			if (sourceMethod == null) {
				return "resolution failure";
			}
			if (stacktrace.Any (frame => frame.ToString ().Contains ("System.Diagnostics.Tracing.EventSource::GetCustomAttributeHelper"))) {
				return "eventsource_customattributehelper";
			}
			if (stacktrace.Any (frame => frame.ToString ().Contains ("EventSource::WriteEventWithRelatedActivityIdCore")) &&
				stacktrace.Any (frame => frame.ToString ().Contains ("TraceLoggingEventTypes::.ctor(System.String"))) {
				return "eventsource_traceloggingeventtypes";
			}
			var ns = sourceMethod.DeclaringType.Namespace;
			if (!String.IsNullOrEmpty (ns)) {
				return ns;
			}
			TypeDefinition type = sourceMethod.DeclaringType;
			while (type.DeclaringType != null) {
				type = type.DeclaringType;
			}
			ns = type.Namespace;
			if (!String.IsNullOrEmpty (ns)) {
				return ns;
			}
			return sourceMethod.DeclaringType.FullName;
			// return "uncategorized";

			// Category category = Category.Uncategorized;
			// if (stacktrace.Any(m => m.Contains("System.Object::GetHashCode()")) &&
			//     stacktrace.Any(m => m.Contains("<>f__AnonymousType0`1"))) {
			//     SetCategory(ref category, Category.AnonymousGetHashCode);
			// }
			// if (stacktrace.Any(m => m.Contains("System.Collections.IEnumerator::MoveNext"))) {
			//     SetCategory(ref category, Category.IEnumeratorMoveNextReflection);
			// }
			// // if (stacktrace.Any(m => m.Contains("System.Object::ToString()")) &&
			// //     stacktrace.Any(m => m.Contains("System.Collections.IEnumerator::MoveNext"))) {
			// //     SetCategory(ref category, Category.ObjectToStringMoveNext);
			// // }
			// if (stacktrace.Any(m => m.Contains("System.Object::Equals")) &&
			//     stacktrace.Any(m => m.Contains("System.Dynamic.BindingRestrictions/TypeRestriction::Equals"))) {
			//     SetCategory(ref category, Category.ObjectEqualsDynamicBinding);
			// }
			// // if (stacktrace.Any(m => m.Contains("System.Resources.ResourceManager::GetString")) &&
			// //     stacktrace.Any(m => m.Contains("System.Collections.IEnumerator::MoveNext"))) {
			// //     SetCategory(ref category, Category.ResourceManagerGetStringMoveNext);
			// // }
			// if (stacktrace.Any(m => m.Contains("System.MulticastDelegate::GetMethodImpl")) &&
			//     stacktrace.Any(m => m.Contains("System.Type::IsSubclassOf"))) {
			//     SetCategory(ref category, Category.MulticastDelegateGetMethodBase);
			// }
			// // if (stacktrace.Any(m => m.Contains("System.Xml.Schema.XmlSchemaCollection")) &&
			// //     stacktrace.Any(m => m.Contains("System.Collections.IEnumerator::MoveNext")) &&
			// //     stacktrace.All(m => !m.Contains("System.Object::ToString()"))) {
			// //     SetCategory(ref category, Category.XmlSchemaMoveNext);
			// // }
			// if (stacktrace.Any(m => m.Contains("IDictionaryEnumerator::get_Value")) &&
			//     stacktrace.Any(m => m.Contains("System.Resources.ResourceReader::DeserializeObject"))) {
			//     SetCategory(ref category, Category.ResourceEnumeratorDeserializeObject);
			// }
			// if (stacktrace.Any(m => m.Contains("System.Collections.Generic.ComparerHelpers::CreateDefaultEqualityComparer"))) {
			//     SetCategory(ref category, Category.DefaultEqualityComparer);
			// }
			// if (stacktrace.Any(m => m.Contains("System.ComponentModel.Design.ServiceContainer/ServiceCollection`1/EmbeddedTypeAwareTypeComparer"))) {
			//     SetCategory(ref category, Category.ComponentModelEqualityComparer);
			// }
			// if (new Regex("System.Reflection.PropertyInfo::.*.et.*Value").IsMatch(stacktrace[stacktrace.Count-1])) {
			//     SetCategory(ref category, Category.PublicReflectionApi);
			// }
			// if (stacktrace.Any(m => m.Contains("System.Net.Http.HttpRequestMessage::.ctor")) &&
			//     stacktrace.Any(m => m.Contains("System.Net.NetEventSource::Format"))) {
			//     SetCategory(ref category, Category.NetEventSourceFormatTypeName);
			// }
			// if (stacktrace.Any(m => m.Contains("System.Exception::ToString")) &&
			//     stacktrace.Any(m => m.Contains("System.Reflection.MemberInfo::IsDefined"))) {
			//     SetCategory(ref category, Category.ExceptionToStringMemberInfo);
			// }
			// 
			// return category;
		}

		HashSet<(Category, Category)> duplicateCategoryErrors;
		void SetCategory (ref Category existing, Category newCategory)
		{
			if (duplicateCategoryErrors == null) {
				duplicateCategoryErrors = new HashSet<(Category, Category)> ();
			}
			if (existing != Category.Uncategorized &&
				!duplicateCategoryErrors.Contains ((newCategory, existing))) {
				Console.WriteLine (newCategory + " already categorized as " + existing);
				duplicateCategoryErrors.Add ((newCategory, existing));
			}
			existing = newCategory;
		}

	}

	public struct AnalyzedStacktrace
	{
		public int source;
		public ReportedStacktrace stacktrace;
		public ApiAnnotation annotation;
		public string category;
	}
}
