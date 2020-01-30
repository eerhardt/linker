using Mono.Cecil;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace Mono.Linker.Analysis
{
	public class ApiAnnotations
	{
		readonly Dictionary<CodeReadinessAspect, List<ApiAnnotation>> _annotations;

		public ApiAnnotations ()
		{
			_annotations = new Dictionary<CodeReadinessAspect, List<ApiAnnotation>> {
				{ CodeReadinessAspect.AssemblyTrim, new List<ApiAnnotation> () },
				{ CodeReadinessAspect.TypeTrim, new List<ApiAnnotation> () },
				{ CodeReadinessAspect.MemberTrim, new List<ApiAnnotation> () },
				{ CodeReadinessAspect.SingleFile, new List<ApiAnnotation> () }
			};
		}

		public void LoadConfiguration (string filePath)
		{
			using (var fileStream = File.OpenRead (filePath))
			using (JsonDocument document = JsonDocument.Parse (fileStream, new JsonDocumentOptions () {
				CommentHandling = JsonCommentHandling.Skip
			})) {
				foreach (var annotationElement in document.RootElement.EnumerateArray ()) {
					string typeName = annotationElement.GetProperty ("Type").GetString ();

					ApiAnnotation annotation = null;
					if (annotationElement.TryGetProperty("Warn", out var warn)) {
						annotation = new WarnApiAnnotation () {
							Message = warn.GetString ()
						};
					}

					if (annotationElement.TryGetProperty("Suppress", out var suppress)) {
						if (annotation != null) {
							throw new Exception ($"Failure reading linker analysis configuration '{filePath}'. Annotation for '{typeName}' specifies both 'Warn' and 'Suppress' properties.");
						}

						annotation = new SuppressApiAnnotation () {
							Reason = suppress.GetString ()
						};
					}

					annotation.TypeFullName = typeName;

					if (annotation == null) {
						throw new Exception ($"Failure reading linker analysis configuration '{filePath}'. Annotation for '{typeName}' doesn't specifies any action property ('Warn' or 'Suppress').");
					}

					foreach (var annotationProperty in annotationElement.EnumerateObject ()) {
						switch (annotationProperty.Name) {
							case "Methods":
								annotation.MethodNames = annotationProperty.Value.EnumerateArray ().Select (a => a.GetString ()).ToArray ();
								break;

							case "Aspect":
								annotation.Aspect = Enum.Parse<CodeReadinessAspect> (annotationProperty.Value.GetString ());
								break;

							case "Category":
								annotation.Category = annotationProperty.Value.GetString ();
								break;
						}
					}

					if (annotation.Aspect == CodeReadinessAspect.None) {
						throw new Exception ($"Failure reading linker analysis configuration '{filePath}'. Annotation for type '{typeName}' doesn't specify 'Aspect' property.");
					}

					_annotations [annotation.Aspect].Add (annotation);
					if (annotation.Aspect == CodeReadinessAspect.AssemblyTrim) {
						_annotations [CodeReadinessAspect.TypeTrim].Add (annotation);
						_annotations [CodeReadinessAspect.MemberTrim].Add (annotation);
					}
					else if (annotation.Aspect == CodeReadinessAspect.TypeTrim) {
						_annotations [CodeReadinessAspect.MemberTrim].Add (annotation);
					}
				}
			}
		}

		public ApiAnnotation GetAnnotation(MethodDefinition method, CodeReadinessAspect aspect)
		{
			string fullTypeName = method.DeclaringType.FullName;
			foreach (var annotation in _annotations [aspect].Where (a => a.TypeFullName == fullTypeName)) {
				string fullMethodName = GetSignature (method);
				if (annotation.MethodNames.Any (methodName => methodName == fullMethodName)) {
					return annotation;
				}
			}

			return null;
		}

		static string GetSignature (MethodDefinition method)
		{
			var builder = new StringBuilder ();
			builder.Append (method.Name);
			if (method.HasGenericParameters) {
				builder.Append ('<');

				for (int i = 0; i < method.GenericParameters.Count - 1; i++)
					builder.Append ($"{method.GenericParameters [i]},");

				builder.Append ($"{method.GenericParameters [method.GenericParameters.Count - 1]}>");
			}

			builder.Append ("(");

			if (method.HasParameters) {
				for (int i = 0; i < method.Parameters.Count - 1; i++) {
					builder.Append ($"{method.Parameters [i].ParameterType},");
				}

				builder.Append (method.Parameters [method.Parameters.Count - 1].ParameterType);
			}

			builder.Append (")");

			return builder.ToString ();
		}
	}
}
