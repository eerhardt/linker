using Mono.Cecil;
using Mono.Linker.Analysis;
using System.IO;

namespace Mono.Linker.Steps
{
	public class AnalysisStep : BaseStep
	{
		readonly LinkContext context;
		readonly CallgraphDependencyRecorder callgraphDependencyRecorder;
		readonly AnalysisReflectionPatternRecorder reflectionPatternRecorder;
		readonly AnalysisEntryPointsStep entryPointsStep;
		readonly AnalysisAnnotationsStep annotationsStep;

		public AnalysisStep(LinkContext context, AnalysisEntryPointsStep entryPointsStep, AnalysisAnnotationsStep annotationsStep)
		{
			this.context = context;
			this.entryPointsStep = entryPointsStep;
			this.annotationsStep = annotationsStep;
			
			callgraphDependencyRecorder = new CallgraphDependencyRecorder ();
			context.Tracer.AddRecorder (callgraphDependencyRecorder);

			reflectionPatternRecorder = new AnalysisReflectionPatternRecorder ();
			context.ReflectionPatternRecorder = reflectionPatternRecorder;
		}

		protected override void Process ()
		{
			var apiFilter = new ApiFilter (reflectionPatternRecorder.UnanalyzedMethods, entryPointsStep.EntryPoints, annotationsStep.ApiAnnotations);
			var cg = new CallGraph (callgraphDependencyRecorder.Dependencies, apiFilter);

			string jsonFile = Path.Combine (context.OutputDirectory, "trimanalysis.json");
			using (StreamWriter sw = new StreamWriter (jsonFile)) {
				(IntCallGraph intCallGraph, IntMapping<MethodDefinition> mapping) = IntCallGraph.CreateFrom (cg);
				var formatter = new Formatter (cg, mapping, json: true, sw);
				var analyzer = new Analyzer (cg, intCallGraph, mapping, apiFilter, reflectionPatternRecorder.ResolvedReflectionCalls, formatter, Grouping.Callee);
				analyzer.Analyze ();
			}
		}
	}
}
