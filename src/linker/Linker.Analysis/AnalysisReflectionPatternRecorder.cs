using Mono.Cecil;
using System.Collections.Generic;

namespace Mono.Linker.Analysis
{
	class AnalysisReflectionPatternRecorder : IReflectionPatternRecorder
	{
		public Dictionary<MethodDefinition, LinkerUnanalyzedAnnotation> UnanalyzedMethods { get; private set; } = 
			new Dictionary<MethodDefinition, LinkerUnanalyzedAnnotation> ();
		public Dictionary<MethodDefinition, HashSet<MethodDefinition>> ResolvedReflectionCalls { get; private set; } =
			new Dictionary<MethodDefinition, HashSet<MethodDefinition>> ();

		public AnalysisReflectionPatternRecorder()
		{
		}

		public void RecognizedReflectionAccessPattern (MethodDefinition sourceMethod, MethodDefinition reflectionMethod, IMemberDefinition accessedItem)
		{
			AddResolvedReflectionCall (sourceMethod, reflectionMethod);
		}

		public void UnrecognizedReflectionAccessPattern (MethodDefinition sourceMethod, MethodDefinition reflectionMethod, 
			CodeReadinessAspect aspect, string message, string category)
		{
			if (!UnanalyzedMethods.TryGetValue (sourceMethod, out var unanalyzedAnnotation)) {
				unanalyzedAnnotation = new LinkerUnanalyzedAnnotation () {
					WarnAnnotations = new List<WarnApiAnnotation> ()
				};
				UnanalyzedMethods.Add (sourceMethod, unanalyzedAnnotation);
			}

			unanalyzedAnnotation.WarnAnnotations.Add (
				new WarnApiAnnotation () {
					Aspect = aspect,
					Type = reflectionMethod.DeclaringType,
					Method = reflectionMethod,
					Category = category ?? "LinkerUnanalyzed",
					Message = message
				});
		}

		private void AddResolvedReflectionCall(MethodDefinition caller, MethodDefinition callee)
		{
			if (!ResolvedReflectionCalls.TryGetValue(callee, out HashSet<MethodDefinition> callers)) {
				callers = new HashSet<MethodDefinition> ();
				ResolvedReflectionCalls.Add (callee, callers);
			}

			callers.Add (caller);
		}
	}
}
