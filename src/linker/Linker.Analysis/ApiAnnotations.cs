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
		[Flags]
		public enum AnnotationAspect
		{
			AssemblyTrim = 0x1,
			TypeTrim = 0x2,
			MemberTrim = 0x4,
			SingleFile = 0x8
		}

		public class ApiAnnotation
		{
			public string TypeFullName { get; set; }
			public IEnumerable<string> MethodNames { get; set; }

			public AnnotationAspect Aspect { get; set; }
			public string Action { get; set; }
			public string Message { get; set; }
		}

		List<ApiAnnotation> _annotations;

		public ApiAnnotations ()
		{
			_annotations = new List<ApiAnnotation> ();
		}

		public void LoadConfiguration (string filePath)
		{
			using (var fileStream = File.OpenRead (filePath))
			using (JsonDocument document = JsonDocument.Parse (fileStream, new JsonDocumentOptions () {
				CommentHandling = JsonCommentHandling.Skip
			})) {
				foreach (var namespaceProperty in document.RootElement.EnumerateObject ()) {
					string namespaceName = namespaceProperty.Name;
					foreach (var typeProperty in namespaceProperty.Value.EnumerateObject ()) {
						string typeName = typeProperty.Name;
						foreach (var methodProperty in typeProperty.Value.EnumerateObject ()) {
							string methodName = methodProperty.Name;
							var apiAnnotation = new ApiAnnotation () {
								TypeFullName = namespaceName + "." + typeName
							};

							foreach (var annotationProperty in methodProperty.Value.EnumerateObject ()) {
								switch (annotationProperty.Name) {
									case "Overrides":
										apiAnnotation.MethodNames = annotationProperty.Value.EnumerateArray ().Select (a => a.GetString ()).ToArray ();
										break;

									case "Aspect":
										if (annotationProperty.Value.ValueKind == JsonValueKind.Array) {
											foreach (var aspectName in annotationProperty.Value.EnumerateArray ().Select (a => a.GetString ())) {
												apiAnnotation.Aspect |= Enum.Parse<AnnotationAspect> (aspectName);
											}
										} else {
											apiAnnotation.Aspect = Enum.Parse<AnnotationAspect> (annotationProperty.Value.GetString ());
										}

										break;

									case "Action":
										apiAnnotation.Action = annotationProperty.Value.GetString ();
										break;

									case "Message":
										apiAnnotation.Message = annotationProperty.Value.GetString ();
										break;
								}
							}

							if (apiAnnotation.MethodNames == null) {
								apiAnnotation.MethodNames = new string [] { methodName };
							}

							if ((apiAnnotation.Aspect & AnnotationAspect.MemberTrim) != 0) {
								apiAnnotation.Aspect |= AnnotationAspect.TypeTrim;
							}

							if ((apiAnnotation.Aspect & AnnotationAspect.TypeTrim) != 0) {
								apiAnnotation.Aspect |= AnnotationAspect.AssemblyTrim;
							}

							_annotations.Add (apiAnnotation);
						}
					}
				}
			}
		}

		public InterestingReason GetReasonForAnnotation(MethodDefinition method)
		{
			string fullTypeName = method.DeclaringType.FullName;
			foreach (var annotation in _annotations.Where (a => a.TypeFullName == fullTypeName)) {
				string fullMethodName = GetSignature (method);
				if (annotation.MethodNames.Any (methodName => methodName == fullMethodName)) {
					return Enum.Parse<InterestingReason> (annotation.Action);
				}
			}

			return InterestingReason.None;
		}

		public static string GetSignature (MethodDefinition method)
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
					// TODO: modifiers
					// TODO: default values
					builder.Append ($"{method.Parameters [i].ParameterType},");
				}

				builder.Append (method.Parameters [method.Parameters.Count - 1].ParameterType);
			}

			builder.Append (")");

			return builder.ToString ();
		}
	}
}
