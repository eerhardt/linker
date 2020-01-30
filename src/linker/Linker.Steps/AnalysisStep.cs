using Mono.Cecil;
using Mono.Linker.Analysis;
using System.IO;
using System.Linq;

namespace Mono.Linker.Steps
{
	public class AnalysisStep : BaseStep
	{
		readonly LinkContext context;
		readonly CallgraphDependencyRecorder callgraphDependencyRecorder;
		readonly AnalysisReflectionPatternRecorder reflectionPatternRecorder;
		readonly AnalysisEntryPointsStep entryPointsStep;

		public AnalysisStep(LinkContext context, AnalysisEntryPointsStep entryPointsStep)
		{
			this.context = context;
			this.entryPointsStep = entryPointsStep;
			
			callgraphDependencyRecorder = new CallgraphDependencyRecorder ();
			context.Tracer.AddRecorder (callgraphDependencyRecorder);

			reflectionPatternRecorder = new AnalysisReflectionPatternRecorder ();
			context.ReflectionPatternRecorder = reflectionPatternRecorder;
		}

		protected override void Process ()
		{
			var annotations = new ApiAnnotations ();

			foreach (var analysisConfigFile in Directory.EnumerateFiles (
				Path.GetDirectoryName (typeof(AnalysisStep).Assembly.Location),
				"*.analysisconfig.jsonc")) {
				annotations.LoadConfiguration (analysisConfigFile);
			}

			foreach (var analysisConfigFile in Directory.EnumerateFiles (
				Path.GetDirectoryName (entryPointsStep.EntryPoints.First ().Module.FileName),
				"*.analysisconfig.jsonc")) {
				annotations.LoadConfiguration (analysisConfigFile);
			}

			var apiFilter = new ApiFilter (reflectionPatternRecorder.UnanalyzedMethods, entryPointsStep.EntryPoints, annotations);
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
