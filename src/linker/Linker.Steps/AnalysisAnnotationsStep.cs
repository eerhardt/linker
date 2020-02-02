using Mono.Linker.Analysis;
using System.IO;
using System.Linq;

namespace Mono.Linker.Steps
{
	public class AnalysisAnnotationsStep : BaseStep
	{
		public ApiAnnotations ApiAnnotations { get; private set; }

		private AnalysisEntryPointsStep entryPointStep;

		public AnalysisAnnotationsStep (AnalysisEntryPointsStep entryPointStep)
		{
			this.entryPointStep = entryPointStep;
		}

		protected override void Process ()
		{
			ApiAnnotations = new ApiAnnotations ();

			foreach (var analysisConfigFile in Directory.EnumerateFiles (
				Path.GetDirectoryName (typeof (AnalysisStep).Assembly.Location),
				"*.analysisconfig.jsonc")) {
				ApiAnnotations.LoadConfiguration (analysisConfigFile, Context);
			}

			foreach (var analysisConfigFile in Directory.EnumerateFiles (
				Path.GetDirectoryName (entryPointStep.EntryPoints.First ().Module.FileName),
				"*.analysisconfig.jsonc")) {
				ApiAnnotations.LoadConfiguration (analysisConfigFile, Context);
			}
		}
	}
}
