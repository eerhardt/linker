using Mono.Cecil;
using System.Collections.Generic;

namespace Mono.Linker.Analysis
{
	class CallgraphDependencyRecorder : IDependencyRecorder
	{
		public List<(MethodDefinition source, MethodDefinition target)> Dependencies { get; } = 
			new List<(MethodDefinition source, MethodDefinition target)> ();

		public void RecordDependency (object source, object target, bool marked)
		{
			if ((source is MethodDefinition sourceMD) && (target is MethodReference targetMR)) {
				var targetMD = targetMR.Resolve ();
				if (targetMD == null)
					return;

				Dependencies.Add ((sourceMD, targetMD));
			}
		}
	}
}
