using Mono.Cecil;
using System.Collections.Generic;
using System.Linq;

namespace Mono.Linker.Analysis
{
	public abstract class ApiAnnotation
	{
		public string TypeFullName { get; set; }
		public TypeDefinition Type { get; set; }

		public MethodDefinition Method { get; set; }

		public CodeReadinessAspect Aspect { get; set; }

		public string Category { get; set; }
	}

	public class WarnApiAnnotation : ApiAnnotation
	{
		public string Message { get; set; }

		public override string ToString ()
		{
			return $"{Category}: {Message}";
		}
	}

	public class LinkerUnanalyzedAnnotation : ApiAnnotation
	{
		public List<WarnApiAnnotation> WarnAnnotations { get; set; }

		public override string ToString ()
		{
			return $"{Category}: {string.Join (" | ", WarnAnnotations.Select (c => c.Message))}";
		}
	}

	public class SuppressApiAnnotation : ApiAnnotation
	{
		public string Reason { get; set; }

		public override string ToString ()
		{
			return $"{Category}: {Reason}";
		}
	}
}
