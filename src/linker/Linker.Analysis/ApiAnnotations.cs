using Mono.Cecil;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace Mono.Linker.Analysis
{
	public class ApiAnnotations
	{
		readonly Dictionary<MethodDefinition, ApiAnnotation> _annotations;

		public ApiAnnotations ()
		{
			_annotations = new Dictionary<MethodDefinition, ApiAnnotation> ();
		}

		public void LoadConfiguration (string filePath, LinkContext linkContext)
		{
			using (var fileStream = File.OpenRead (filePath))
			using (JsonDocument document = JsonDocument.Parse (fileStream, new JsonDocumentOptions () {
				CommentHandling = JsonCommentHandling.Skip
			})) {
				foreach (var annotationElement in document.RootElement.EnumerateArray ()) {
					string typeName = annotationElement.GetProperty ("Type").GetString ();

					// Try to resolve the type - if it can't be resolved - issues a warning
					TypeDefinition typeDefinition = linkContext.GetType (typeName);
					if (typeDefinition == null) {
						linkContext.LogMessage (MessageImportance.High,	$"Could not resolve type name '{typeName}' specified in link analysis configuration file '{filePath}'. The annotation with this type name will be ignored.");
						continue;
					}

					Func<ApiAnnotation> annotationCreator = null;
					if (annotationElement.TryGetProperty("Warn", out var warn)) {
						annotationCreator = () => new WarnApiAnnotation () {
							Message = warn.GetString ()
						};
					}

					if (annotationElement.TryGetProperty("Suppress", out var suppress)) {
						if (annotationCreator != null) {
							throw new Exception ($"Failure reading linker analysis configuration '{filePath}'. Annotation for '{typeName}' specifies both 'Warn' and 'Suppress' properties.");
						}

						annotationCreator = () => new SuppressApiAnnotation () {
							Reason = suppress.GetString ()
						};
					}

					if (annotationCreator == null) {
						throw new Exception ($"Failure reading linker analysis configuration '{filePath}'. Annotation for '{typeName}' doesn't specifies any action property ('Warn' or 'Suppress').");
					}

					string [] methodNames = null;
					CodeReadinessAspect aspect = CodeReadinessAspect.None;
					string category = null;
					foreach (var annotationProperty in annotationElement.EnumerateObject ()) {
						switch (annotationProperty.Name) {
							case "Methods":
								methodNames = annotationProperty.Value.EnumerateArray ().Select (a => a.GetString ()).ToArray ();
								break;

							case "Aspect":
								aspect = Enum.Parse<CodeReadinessAspect> (annotationProperty.Value.GetString ());
								break;

							case "Category":
								category = annotationProperty.Value.GetString ();
								break;
						}
					}

					if (methodNames == null) {
						throw new Exception ($"Failure reading linker analysis configuration '{filePath}'. Annotation for type '{typeName}' doesn't specify 'Methods' property.");
					}

					if (aspect == CodeReadinessAspect.None) {
						throw new Exception ($"Failure reading linker analysis configuration '{filePath}'. Annotation for type '{typeName}' doesn't specify 'Aspect' property.");
					}

					// Try to resolve all methods and warn if it fails
					foreach (var methodName in methodNames) {
						bool foundMethod = false;
						foreach (var method in typeDefinition.Methods.Where (m => MatchesMethodName (methodName, m))) {
							foundMethod = true;

							var annotation = annotationCreator ();
							annotation.Aspect = aspect;
							annotation.Category = category;
							annotation.Method = method;
							annotation.Type = typeDefinition;
							annotation.TypeFullName = typeName;

							if (_annotations.ContainsKey (method)) {
								linkContext.LogMessage (MessageImportance.High, $"@@@@@ Method '{method}' already has an annotation, trying to add another one is invalid.");
							} else {
								_annotations [method] = annotation;
							}
						}

						if (!foundMethod) {
							linkContext.LogMessage (MessageImportance.High, $"Annotation for type '{typeName}' in '{filePath}' contains a method name '{methodName}' which can't be resolved. Make sure the method is correctly specified.");
						}
					}
				}
			}
		}

		public void ProcessLoadedAnnotations (AnnotationStore store)
		{
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

			Queue<WarnApiAnnotation> annotationsToPropagate = new Queue<WarnApiAnnotation> ();

			foreach (var annotation in _annotations.Values.OfType<WarnApiAnnotation> ()) {
				if (annotation.Method.IsVirtual) {
					annotationsToPropagate.Enqueue (annotation);
				}
			}

			while (annotationsToPropagate.Count > 0) {
				var annotation = annotationsToPropagate.Dequeue ();
				var overrides = store.GetOverrides (annotation.Method);
				if (overrides == null)
					continue;

				foreach (var overrideMethod in overrides) {
					var overrideMethodDefinition = overrideMethod.Override;

					_annotations.TryGetValue (overrideMethodDefinition, out var existingAnnotation);
					if (existingAnnotation == null || existingAnnotation is LinkerUnanalyzedAnnotation) {
						if (existingAnnotation != null) {
							Console.WriteLine ($"Overwriting annotation {existingAnnotation}");
						}

						// Console.WriteLine ($"Adding implied annotation {warnAnnotation} to {overrideMethodDefinition} because it overrides annotated {methodDefinition}");
						var overrideAnnotation = new WarnApiAnnotation () {
							Aspect = annotation.Aspect,
							Category = annotation.Category,
							Method = overrideMethodDefinition,
							Message = annotation.Message
						};

						_annotations.Add (overrideMethodDefinition, overrideAnnotation);

						// Propagate recuresively
						annotationsToPropagate.Enqueue (overrideAnnotation);
					} else {
						Console.WriteLine ($"### {overrideMethodDefinition}{Environment.NewLine}  Skipping application of implied annotation since it already has one.{Environment.NewLine}   {annotation}{Environment.NewLine}   {annotation.Method}");
					}
				}
			}
		}

		public ApiAnnotation GetAnnotation(MethodDefinition method, CodeReadinessAspect aspect)
		{
			if (_annotations.TryGetValue (method, out var annotation)) {
				return annotation;
			}

			return null;
		}

		static bool MatchesMethodName (string methodName, MethodDefinition method)
		{
			if (methodName == "*") {
				return true;
			}
			else if (methodName.IndexOf ('(') == -1) {
				return methodName == method.Name;
			}
			else {
				return methodName == TypeChecker.GetSignature (method);
			}
		}
	}
}
