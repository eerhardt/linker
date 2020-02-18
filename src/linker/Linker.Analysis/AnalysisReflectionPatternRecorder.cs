using Mono.Cecil;
using System.Collections.Generic;

namespace Mono.Linker.Analysis
{
	class AnalysisReflectionPatternRecorder : IReflectionPatternRecorder
	{
		public Dictionary<MethodDefinition, List<(MethodDefinition ReflectionMethod, CodeReadinessAspect Aspect, string Message)>> UnanalyzedMethods { get; private set; } = 
			new Dictionary<MethodDefinition, List<(MethodDefinition, CodeReadinessAspect, string)>> ();
		public Dictionary<MethodDefinition, HashSet<MethodDefinition>> ResolvedReflectionCalls { get; private set; } =
			new Dictionary<MethodDefinition, HashSet<MethodDefinition>> ();

		public AnalysisReflectionPatternRecorder()
		{
		}

		public void RecognizedReflectionAccessPattern (MethodDefinition sourceMethod, MethodDefinition reflectionMethod, IMemberDefinition accessedItem)
		{
			AddResolvedReflectionCall (sourceMethod, reflectionMethod);
		}

		public void UnrecognizedReflectionAccessPattern (MethodDefinition sourceMethod, MethodDefinition reflectionMethod, CodeReadinessAspect aspect, string message)
		{
			if (!UnanalyzedMethods.TryGetValue (sourceMethod, out var existingRecords)) {
				existingRecords = new List<(MethodDefinition, CodeReadinessAspect, string)> ();
				UnanalyzedMethods.Add (sourceMethod, existingRecords);
			}

			existingRecords.Add ((reflectionMethod, aspect, message));
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
