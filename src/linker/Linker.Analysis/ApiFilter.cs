using System;
using System.Collections.Generic;
using System.Linq;
using Mono.Cecil;

namespace Mono.Linker.Analysis
{
	public enum InterestingReasonKind
	{
		None,
		LinkerUnanalyzed,
		// AnnotatedLinkerUnfriendly,

		AnnotatedLinkerFriendly, // keep track of the callstacks to methods we've explicitly marked friendly.
								 // known-good APIs may reach reflection, but the scan tool will treat this as ok.
								 // the search will stop when such an API is hit.
								 // in the analysis, treat these as if they have no callees.
								 // they will not be considered interesting, and as such will not show up in the output.

		CachedReflectionInfo, // could be made linker-safe with a simple fix.
		SerializationBigHammer,

		ElementBoxStorage,
		LocalBoxStorage,
		TypeConverterConvertToSimple,
		XmlSerializationILGenSimple,
		LinqCompileReflection,
		ExportDefaultValue,
		XmlSerializationSourceInfoSimple,
		XmlSerializationCodeGeneratorSimple,
		JsonFormatGeneratorStatics,
		CodeGeneratorStatic,
		XmlFormatGeneratorStatics,
		ExpandoObjectSimple,
		CallSiteHelpersSimple,
		DelegateHelpersStatics,
		CodeGeneratorGettersSimple,
		ActivatorCacheSimple,
		OffsetOfSimple,

		// give a reason for each annotated linker unfriendly API:
		// such an API may call many other unsafe APIs, but
		// we don't care. stop the search at these, and only report this far.
		// in the analysis, treat these as if they have no callees.
		EventSourceBigHammer,
		CustomResourceSet,
		SourceInfo,
		ResourceBinaryFormatter,
		ReadingResources,
		LinqExpressionCompilation,
		DispatchProxy,
		XmlSerialization,
		XmlSchemaMapping,
		LazyOfT,
		SystemSRResourceManagerCreation,
		TypeConverterConvertToComplex,
		GenerateRefEmitAssembly,
		EmitNewHoistedLocals,
		DefaultValueAttribute,
		ThreadPrincipal,
		DefineDefaultConstructor,
		EventSourceGetCustomAttributeHelperLooksSafe,
		ComponentActivator,
		CallSiteBinderSimple,
		GetEnumeratorElementType, // *maybe* ok - looks for IEnumerable things like Current, Add, GetEnumerator, IEnumerable<* implementation
		XmlILGeneratorBakeMethodsReflection,
		CollectionDataContractRequiresAddMethodMaybeOk,

		ComponentModelAttributeRequiresCtor,
		ComponentModelAttributeRequiresDefaultField,
		ComponentModelFindMethod,
		ComponentModelAttributeRequiresType,
		ComGetAttributesSimple,
		DataColumnGetsNullPropertyOfType,
		DbProviderType,
		ResourceTypeReflection,
		ComponentValidatorReflection,
		JsonConverterAttributeRequiresCtor,
		EarlyBoundInfoRequiresCtor,
		XmlSerializationGetMethodFromType,
		AttributeParentReflection,
		AttributeNamedParamReflection,
		ComponentModelBindingListRequiresCtor,
		IDOBinderReflection,
		DeserializationValueTypeFixup,
		LinqGetMethodWrapper,
		LinqCheckMethod,
		DeserializeDataTableGetType,
		SqlUdtStorageStaticNull,

		XmlSerializationRequiresAddMethod,
		XmlReflectionGetMethod,
		XmlSerializationGetConstructorFlags,
		XMLSchemaSetPropertiesGetType,
		CallSiteSimple, // similar to CallSiteBinderSimple?
		RuntimeBinderReflection,
		XmlSoapImporterFieldModelReflection,
		XmlReflectionImporter,
		XmlSerializationReaderCollectionAdd,
		BinaryFormatterGetType,
		XmlSerializationReaderGetMethod,
		DataContractExporterCodeGenReflection,
		XmlFormatReaderGeneratorSafe,
		RuntimeBinderReflectionSafe,
		DataContractSimple,
		XmlFormatReaderGeneratorMaybeSafe,

		CryptoConfig,
		OffsetOf,
		GetMethod,
		GetConstructor,
		DataStorageGetType,
		XmlSchemaProviderAttributeMethodName,
		XmlILGenGetCurrent,
		CertificateDownloader,
		LinqGetValue,
		XsltGetMethod,

		// common case could be detected by linker
		LinqExpressionCallString,

		LinqExpressionCallMaybeSafe,
		LinqExpressionFieldString,

		DataContractSerializationReflection,

		// should be fixed by preservedependency
		CryptoConfigForwarderReflectionDependency,

		// TypeBuilder public entry points
		TypeBuilder,

		// PInvoke,
		KnownReflection,
		KnownReflection_EnumMetadata,

		// If the linker is run with --used-attrs-only it trims attributes which are not explicitly marked elsewhere
		// this can lead to missing attributes in some reflection cases.
		// The problem typically comes in two flavors:
		//  - enumeration - simply enumerating all attributes on a given item will return different results as this is not considered
		//                  by the linker - and if there are no other references to the attribute, it will be trimmed.
		//  - inheritance - asking for an attribute via its base type will also break as linker will not see an explicit reference
		//                  and trim the attribute.
		KeepUsedAttributeTypesOnlyUnsafe,

		// what kind?
		CreateInstance,

		// Marks places where new generic instantiations are create in dynamic way which linker can't probably understand.
		// Right now this is probably safe, but once linker starts full generic instantiation analysis and relies on it
		// this will be unsafe.
		DynamicGenericInstantiation,

		ToInvestigate,
		ToInvestigate_EnumMetadata,

		// Single-file unfriendly APIs
		SingleFileUnfriendly,

		// linker sometimes warns when it doesn't detect inputs to some reflection-like API.
		// but there are some APIs where it doesn't look for the right signature, and warns
		// even when it's safe.
		LinkerShouldNotWarn,
	}

	public class InterestingReason
	{
		public CodeReadinessAspect Aspect { get; private set; }
		public InterestingReasonKind Kind { get; private set; }
		public string Message { get; private set; }

		public InterestingReason (CodeReadinessAspect aspect, InterestingReasonKind kind, string message)
		{
			Aspect = aspect;
			Kind = kind;
			Message = message;
		}

		public static InterestingReason AnnotatedLinkerFriendly =
			new InterestingReason (CodeReadinessAspect.AssemblyTrim, InterestingReasonKind.AnnotatedLinkerFriendly, "Linker friendly");

		public static InterestingReason LinkerShouldNotWarn =
			new InterestingReason (CodeReadinessAspect.AssemblyTrim, InterestingReasonKind.LinkerShouldNotWarn, "We think the linker should not warn in this case, but it does");


		public static InterestingReason SerializationBigHammer =
			new InterestingReason (CodeReadinessAspect.TypeTrim, InterestingReasonKind.SerializationBigHammer, "Marks everything in serialization namespaces as potentially unsafe");

		public static InterestingReason EventSourceBigHammer =
			new InterestingReason (CodeReadinessAspect.MemberTrim, InterestingReasonKind.EventSourceBigHammer, "Marks everything around event sources as potentially unsafe - but only for member level trimming");


		public static InterestingReason GetMethod =
			new InterestingReason (CodeReadinessAspect.TypeTrim, InterestingReasonKind.GetMethod, "GetMethod by name");
		
		public static InterestingReason GetConstructor =
			new InterestingReason (CodeReadinessAspect.TypeTrim, InterestingReasonKind.GetConstructor, "Requries ctor to be present on the type");


		public static InterestingReason ResourceBinaryFormatter =
			new InterestingReason (CodeReadinessAspect.TypeTrim, InterestingReasonKind.ResourceBinaryFormatter, "???");

		public static InterestingReason ReadingResources =
			new InterestingReason (CodeReadinessAspect.TypeTrim, InterestingReasonKind.ReadingResources, "reading resources has code to do deserialization. some reflection is used upfront even in the general case.");

		public static InterestingReason LinqExpressionCompilation =
			new InterestingReason (CodeReadinessAspect.TypeTrim, InterestingReasonKind.LinqExpressionCompilation, "???");

		public static InterestingReason LinqExpressionCallString =
			new InterestingReason (CodeReadinessAspect.TypeTrim, InterestingReasonKind.LinqExpressionCallString, "???");

		public static InterestingReason LinqCompileReflection =
			new InterestingReason (CodeReadinessAspect.TypeTrim, InterestingReasonKind.LinqCompileReflection, "???");

		public static InterestingReason LinqGetMethodWrapper =
			new InterestingReason (CodeReadinessAspect.TypeTrim, InterestingReasonKind.LinqGetMethodWrapper, "???");

		public static InterestingReason LinqCheckMethod =
			new InterestingReason (CodeReadinessAspect.TypeTrim, InterestingReasonKind.LinqCheckMethod, "???");

		public static InterestingReason LinqExpressionCallMaybeSafe =
			new InterestingReason (CodeReadinessAspect.TypeTrim, InterestingReasonKind.LinqExpressionCallMaybeSafe, "???");

		public static InterestingReason LinqExpressionFieldString =
			new InterestingReason (CodeReadinessAspect.TypeTrim, InterestingReasonKind.LinqExpressionFieldString, "???");

		public static InterestingReason LinqGetValue =
			new InterestingReason (CodeReadinessAspect.TypeTrim, InterestingReasonKind.LinqGetValue, "???");


		public static InterestingReason DispatchProxy =
			new InterestingReason (CodeReadinessAspect.TypeTrim, InterestingReasonKind.DispatchProxy, "???");

		public static InterestingReason XmlSerialization =
			new InterestingReason (CodeReadinessAspect.TypeTrim, InterestingReasonKind.XmlSerialization, "???");

		public static InterestingReason XmlSchemaMapping =
			new InterestingReason (CodeReadinessAspect.TypeTrim, InterestingReasonKind.XmlSchemaMapping, "???");

		public static InterestingReason XmlSerializationILGenSimple =
			new InterestingReason (CodeReadinessAspect.TypeTrim, InterestingReasonKind.XmlSerializationILGenSimple, "???");

		public static InterestingReason XmlSerializationSourceInfoSimple =
			new InterestingReason (CodeReadinessAspect.TypeTrim, InterestingReasonKind.XmlSerializationSourceInfoSimple, "???");

		public static InterestingReason XmlSerializationCodeGeneratorSimple =
			new InterestingReason (CodeReadinessAspect.TypeTrim, InterestingReasonKind.XmlSerializationCodeGeneratorSimple, "???");

		public static InterestingReason XmlSerializationGetMethodFromType =
			new InterestingReason (CodeReadinessAspect.TypeTrim, InterestingReasonKind.XmlSerializationGetMethodFromType, "???");

		public static InterestingReason GetEnumeratorElementType =
			new InterestingReason (CodeReadinessAspect.TypeTrim, InterestingReasonKind.GetEnumeratorElementType, "???");

		public static InterestingReason XmlILGeneratorBakeMethodsReflection =
			new InterestingReason (CodeReadinessAspect.TypeTrim, InterestingReasonKind.XmlILGeneratorBakeMethodsReflection, "???");

		public static InterestingReason XmlSerializationRequiresAddMethod =
			new InterestingReason (CodeReadinessAspect.TypeTrim, InterestingReasonKind.XmlSerializationRequiresAddMethod, "???");

		public static InterestingReason XmlReflectionGetMethod =
			new InterestingReason (CodeReadinessAspect.TypeTrim, InterestingReasonKind.XmlReflectionGetMethod, "???");

		public static InterestingReason XmlSerializationGetConstructorFlags =
			new InterestingReason (CodeReadinessAspect.TypeTrim, InterestingReasonKind.XmlSerializationGetConstructorFlags, "???");

		public static InterestingReason XMLSchemaSetPropertiesGetType =
			new InterestingReason (CodeReadinessAspect.TypeTrim, InterestingReasonKind.XMLSchemaSetPropertiesGetType, "???");

		public static InterestingReason XmlSoapImporterFieldModelReflection =
			new InterestingReason (CodeReadinessAspect.TypeTrim, InterestingReasonKind.XmlSoapImporterFieldModelReflection, "???");

		public static InterestingReason XmlReflectionImporter =
			new InterestingReason (CodeReadinessAspect.TypeTrim, InterestingReasonKind.XmlReflectionImporter, "???");

		public static InterestingReason XmlSerializationReaderCollectionAdd =
			new InterestingReason (CodeReadinessAspect.TypeTrim, InterestingReasonKind.XmlSerializationReaderCollectionAdd, "???");

		public static InterestingReason XmlILGenGetCurrent =
			new InterestingReason (CodeReadinessAspect.TypeTrim, InterestingReasonKind.XmlILGenGetCurrent, "???");

		public static InterestingReason XmlSerializationReaderGetMethod =
			new InterestingReason (CodeReadinessAspect.TypeTrim, InterestingReasonKind.XmlSerializationReaderGetMethod, "???");


		public static InterestingReason XsltGetMethod =
			new InterestingReason (CodeReadinessAspect.TypeTrim, InterestingReasonKind.XsltGetMethod, "???");


		public static InterestingReason ExportDefaultValue =
			new InterestingReason (CodeReadinessAspect.TypeTrim, InterestingReasonKind.ExportDefaultValue, "???");

		public static InterestingReason CachedReflectionInfo =
			new InterestingReason (CodeReadinessAspect.TypeTrim, InterestingReasonKind.CachedReflectionInfo, "???");

		public static InterestingReason ElementBoxStorage =
			new InterestingReason (CodeReadinessAspect.TypeTrim, InterestingReasonKind.ElementBoxStorage, "???");

		public static InterestingReason LocalBoxStorage =
			new InterestingReason (CodeReadinessAspect.TypeTrim, InterestingReasonKind.LocalBoxStorage, "???");

		public static InterestingReason TypeConverterConvertToSimple =
			new InterestingReason (CodeReadinessAspect.TypeTrim, InterestingReasonKind.TypeConverterConvertToSimple, "???");

		public static InterestingReason ExpandoObjectSimple =
			new InterestingReason (CodeReadinessAspect.TypeTrim, InterestingReasonKind.ExpandoObjectSimple, "???");

		public static InterestingReason CallSiteHelpersSimple =
			new InterestingReason (CodeReadinessAspect.TypeTrim, InterestingReasonKind.CallSiteHelpersSimple, "???");

		public static InterestingReason DelegateHelpersStatics =
			new InterestingReason (CodeReadinessAspect.TypeTrim, InterestingReasonKind.DelegateHelpersStatics, "???");

		public static InterestingReason TypeConverterConvertToComplex =
			new InterestingReason (CodeReadinessAspect.TypeTrim, InterestingReasonKind.TypeConverterConvertToComplex, "???");

		public static InterestingReason GenerateRefEmitAssembly =
			new InterestingReason (CodeReadinessAspect.TypeTrim, InterestingReasonKind.GenerateRefEmitAssembly, "???");

		public static InterestingReason ActivatorCacheSimple =
			new InterestingReason (CodeReadinessAspect.TypeTrim, InterestingReasonKind.ActivatorCacheSimple, "???");

		public static InterestingReason EmitNewHoistedLocals =
			new InterestingReason (CodeReadinessAspect.TypeTrim, InterestingReasonKind.EmitNewHoistedLocals, "???");

		public static InterestingReason DefaultValueAttribute =
			new InterestingReason (CodeReadinessAspect.TypeTrim, InterestingReasonKind.DefaultValueAttribute, "???");

		public static InterestingReason ThreadPrincipal =
			new InterestingReason (CodeReadinessAspect.TypeTrim, InterestingReasonKind.ThreadPrincipal, "???");

		public static InterestingReason EventSourceGetCustomAttributeHelperLooksSafe =
			new InterestingReason (CodeReadinessAspect.TypeTrim, InterestingReasonKind.EventSourceGetCustomAttributeHelperLooksSafe, "???");

		public static InterestingReason ComponentActivator =
			new InterestingReason (CodeReadinessAspect.TypeTrim, InterestingReasonKind.ComponentActivator, "???");

		public static InterestingReason ComponentModelAttributeRequiresCtor =
			new InterestingReason (CodeReadinessAspect.TypeTrim, InterestingReasonKind.ComponentModelAttributeRequiresCtor, "???");

		public static InterestingReason ComponentModelAttributeRequiresDefaultField =
			new InterestingReason (CodeReadinessAspect.TypeTrim, InterestingReasonKind.ComponentModelAttributeRequiresDefaultField, "???");

		public static InterestingReason ComponentModelFindMethod =
			new InterestingReason (CodeReadinessAspect.TypeTrim, InterestingReasonKind.ComponentModelFindMethod, "???");

		public static InterestingReason ComponentModelAttributeRequiresType =
			new InterestingReason (CodeReadinessAspect.TypeTrim, InterestingReasonKind.ComponentModelAttributeRequiresType, "???");

		public static InterestingReason ComponentModelBindingListRequiresCtor =
			new InterestingReason (CodeReadinessAspect.TypeTrim, InterestingReasonKind.ComponentModelBindingListRequiresCtor, "???");


		public static InterestingReason CryptoConfig =
			new InterestingReason (CodeReadinessAspect.TypeTrim, InterestingReasonKind.CryptoConfig, "???");

		public static InterestingReason CryptoConfigForwarderReflectionDependency =
			new InterestingReason (CodeReadinessAspect.TypeTrim, InterestingReasonKind.CryptoConfigForwarderReflectionDependency, "???");

		public static InterestingReason CertificateDownloader =
			new InterestingReason (CodeReadinessAspect.TypeTrim, InterestingReasonKind.CertificateDownloader, "???");


		public static InterestingReason DataColumnGetsNullPropertyOfType =
			new InterestingReason (CodeReadinessAspect.TypeTrim, InterestingReasonKind.DataColumnGetsNullPropertyOfType, "???");

		public static InterestingReason DbProviderType =
			new InterestingReason (CodeReadinessAspect.TypeTrim, InterestingReasonKind.DbProviderType, "???");

		public static InterestingReason ResourceTypeReflection =
			new InterestingReason (CodeReadinessAspect.TypeTrim, InterestingReasonKind.ResourceTypeReflection, "???");

		public static InterestingReason ComponentValidatorReflection =
			new InterestingReason (CodeReadinessAspect.TypeTrim, InterestingReasonKind.ComponentValidatorReflection, "???");

		public static InterestingReason JsonConverterAttributeRequiresCtor =
			new InterestingReason (CodeReadinessAspect.TypeTrim, InterestingReasonKind.JsonConverterAttributeRequiresCtor, "???");

		public static InterestingReason EarlyBoundInfoRequiresCtor =
			new InterestingReason (CodeReadinessAspect.TypeTrim, InterestingReasonKind.EarlyBoundInfoRequiresCtor, "???");


		public static InterestingReason AttributeParentReflection =
			new InterestingReason (CodeReadinessAspect.TypeTrim, InterestingReasonKind.AttributeParentReflection, "???");

		public static InterestingReason AttributeNamedParamReflection =
			new InterestingReason (CodeReadinessAspect.TypeTrim, InterestingReasonKind.AttributeNamedParamReflection, "???");


		public static InterestingReason SourceInfo =
			new InterestingReason (CodeReadinessAspect.TypeTrim, InterestingReasonKind.SourceInfo, "???");


		public static InterestingReason IDOBinderReflection =
			new InterestingReason (CodeReadinessAspect.TypeTrim, InterestingReasonKind.IDOBinderReflection, "???");


		public static InterestingReason CallSiteBinderSimple =
			new InterestingReason (CodeReadinessAspect.TypeTrim, InterestingReasonKind.CallSiteBinderSimple, "???");

		public static InterestingReason CallSiteSimple =
			new InterestingReason (CodeReadinessAspect.TypeTrim, InterestingReasonKind.CallSiteSimple, "???");


		public static InterestingReason DeserializeDataTableGetType =
			new InterestingReason (CodeReadinessAspect.TypeTrim, InterestingReasonKind.DeserializeDataTableGetType, "???");

		public static InterestingReason SqlUdtStorageStaticNull =
			new InterestingReason (CodeReadinessAspect.TypeTrim, InterestingReasonKind.SqlUdtStorageStaticNull, "???");

		public static InterestingReason DataStorageGetType =
			new InterestingReason (CodeReadinessAspect.TypeTrim, InterestingReasonKind.DataStorageGetType, "???");

		public static InterestingReason RuntimeBinderReflection =
			new InterestingReason (CodeReadinessAspect.TypeTrim, InterestingReasonKind.RuntimeBinderReflection, "???");

		public static InterestingReason RuntimeBinderReflectionSafe =
			new InterestingReason (CodeReadinessAspect.TypeTrim, InterestingReasonKind.RuntimeBinderReflectionSafe, "???");


		public static InterestingReason CodeGeneratorStatic =
			new InterestingReason (CodeReadinessAspect.TypeTrim, InterestingReasonKind.CodeGeneratorStatic, "???");

		public static InterestingReason CodeGeneratorGettersSimple =
			new InterestingReason (CodeReadinessAspect.TypeTrim, InterestingReasonKind.CodeGeneratorGettersSimple, "???");

		public static InterestingReason DeserializationValueTypeFixup =
			new InterestingReason (CodeReadinessAspect.TypeTrim, InterestingReasonKind.DeserializationValueTypeFixup, "???");

		public static InterestingReason CollectionDataContractRequiresAddMethodMaybeOk =
			new InterestingReason (CodeReadinessAspect.TypeTrim, InterestingReasonKind.CollectionDataContractRequiresAddMethodMaybeOk, "???");

		public static InterestingReason DataContractSerializationReflection =
			new InterestingReason (CodeReadinessAspect.TypeTrim, InterestingReasonKind.DataContractSerializationReflection, "???");

		public static InterestingReason DataContractExporterCodeGenReflection =
			new InterestingReason (CodeReadinessAspect.TypeTrim, InterestingReasonKind.DataContractExporterCodeGenReflection, "???");

		public static InterestingReason DataContractSimple =
			new InterestingReason (CodeReadinessAspect.TypeTrim, InterestingReasonKind.DataContractSimple, "???");

		public static InterestingReason JsonFormatGeneratorStatics =
			new InterestingReason (CodeReadinessAspect.TypeTrim, InterestingReasonKind.JsonFormatGeneratorStatics, "???");

		public static InterestingReason XmlFormatGeneratorStatics =
			new InterestingReason (CodeReadinessAspect.TypeTrim, InterestingReasonKind.XmlFormatGeneratorStatics, "???");

		public static InterestingReason XmlSchemaProviderAttributeMethodName =
			new InterestingReason (CodeReadinessAspect.TypeTrim, InterestingReasonKind.XmlSchemaProviderAttributeMethodName, "???");

		public static InterestingReason XmlFormatReaderGeneratorSafe =
			new InterestingReason (CodeReadinessAspect.TypeTrim, InterestingReasonKind.XmlFormatReaderGeneratorSafe, "???");

		public static InterestingReason XmlFormatReaderGeneratorMaybeSafe =
			new InterestingReason (CodeReadinessAspect.TypeTrim, InterestingReasonKind.XmlFormatReaderGeneratorMaybeSafe, "???");

		public static InterestingReason BinaryFormatterGetType =
			new InterestingReason (CodeReadinessAspect.TypeTrim, InterestingReasonKind.BinaryFormatterGetType, "???");
	}

	public class ApiFilter
	{
		readonly Dictionary<MethodDefinition, LinkerUnanalyzedAnnotation> unanalyzedMethods;
		readonly HashSet<MethodDefinition> entryMethods;
		readonly ApiAnnotations apiAnnotations;

		public ApiFilter ()
		{
			// don't use any predetermined unanalyzed methods (from the linker)
		}

		public ApiFilter (Dictionary<MethodDefinition, LinkerUnanalyzedAnnotation> unanalyzedMethods, HashSet<MethodDefinition> entryMethods, ApiAnnotations apiAnnotations)
		{
			this.unanalyzedMethods = unanalyzedMethods;
			this.entryMethods = entryMethods;
			this.apiAnnotations = apiAnnotations;
		}

		public bool IsEntryMethod (MethodDefinition method)
		{
			if (entryMethods != null) {
				return entryMethods.Contains (method);
			}
			if (method.IsPublic || method.IsFamily || method.IsFamilyOrAssembly) {
				var t = method.DeclaringType;
				if (t.IsPublic || t.IsNestedPublic || t.IsNestedFamily || t.IsNestedFamilyOrAssembly) {
					return true;
				}
			}
			return false;
		}

		public bool IsInterestingMethod (MethodDefinition method)
		{
			var annotation = GetApiAnnotation (method);
			if (annotation == null || annotation is WarnApiAnnotation)
				return false;

			return true;
		}

		public bool IsAnnotatedLinkerFriendlyApi (MethodDefinition method)
		{
			return GetApiAnnotation (method) is SuppressApiAnnotation;
		}

		public ApiAnnotation GetApiAnnotation (MethodDefinition method)
		{
			CodeReadinessAspect requestedAspect = CodeReadinessAspect.MemberTrim;

			if (unanalyzedMethods.TryGetValue (method, out var unanalyzedAnnotation)) {
				return unanalyzedAnnotation;
			}

			ApiAnnotation annotation = this.apiAnnotations.GetAnnotation (method, requestedAspect);
			if (annotation != null) {
				return annotation;
			}

			//InterestingReason reason = GetInterestingReason (method);
			//if (reason != null && IsApplicableAnnotation (reason.Aspect, requestedAspect)) {
			//	if (reason.Kind == InterestingReasonKind.AnnotatedLinkerFriendly) {
			//		return new SuppressApiAnnotation () {
			//			TypeFullName = method.DeclaringType.FullName,
			//			MethodNames = new string [] { method.Name },
			//			Aspect = reason.Aspect,
			//			Category = reason.Kind.ToString (),
			//			Reason = reason.Message
			//		};
			//	} else {
			//		return new WarnApiAnnotation () {
			//			TypeFullName = method.DeclaringType.FullName,
			//			MethodNames = new string [] { method.Name },
			//			Aspect = reason.Aspect,
			//			Category = reason.Kind.ToString (),
			//			Message = reason.Message
			//		};
			//	}
			//}

			return null;
		}

		private static bool IsApplicableAnnotation (CodeReadinessAspect annotationAspect, CodeReadinessAspect requestedAspect)
		{
			if (requestedAspect == annotationAspect)
				return true;

			if (requestedAspect == CodeReadinessAspect.TypeTrim &&
				annotationAspect == CodeReadinessAspect.AssemblyTrim)
				return true;

			if (requestedAspect == CodeReadinessAspect.MemberTrim &&
				(annotationAspect == CodeReadinessAspect.TypeTrim || annotationAspect == CodeReadinessAspect.AssemblyTrim))
				return true;

			return false;
		}

		InterestingReason GetInterestingReasonFromAnnotationToInvestigate (MethodDefinition method)
		{
			//
			// TODO: re-investigate these reasons. I've learned more about what makes a method
			// safe or unsafe since adding these, and some may be inappropriate.
			//
			if (method.DeclaringType.FullName == "System.Resources.ResourceReader" &&
				method.Name == "InitializeBinaryFormatter") {
				return InterestingReason.ResourceBinaryFormatter;
			}
			// reading resources has code to do deserialization. some reflection is used upfront even in the general case.
			if (method.DeclaringType.FullName == "System.Resources.RuntimeResourceSet") {
				return InterestingReason.ReadingResources;
			}
			if (method.DeclaringType.FullName == "System.Resources.ResourceReader/ResourceEnumerator") {
				return InterestingReason.ReadingResources;
			}
			// if (method.DeclaringType.FullName == "System.Resources.NeutralResourcesLanguageAttribute") {
			//     return InterestingReason.NeutralResourcesLanguageAttribute;
			// }
			if (method.DeclaringType.FullName == "System.Linq.Expressions.LambdaExpression" &&
				method.Name == "Compile") {
				return InterestingReason.LinqExpressionCompilation;
			}
			if (method.DeclaringType.FullName == "System.Linq.Expressions.Expression`1" &&
				method.Name == "Compile") {
				return InterestingReason.LinqExpressionCompilation;
			}
			if (method.DeclaringType.FullName == "System.Linq.Expressions.Expression") {
				if (method.Name == "Power" ||
					method.Name == "PowerAssign") {
					return InterestingReason.LinqExpressionCompilation;
				}
			}
			if (method.DeclaringType.FullName == "System.Reflection.DispatchProxy" &&
				method.Name == "Create") {
				return InterestingReason.DispatchProxy;
			}
			//
			// Xml serialization
			//
			if (method.DeclaringType.FullName == "System.Xml.Serialization.XmlSerializer") {
				if (method.Name == "FromMappings" ||
					method.Name == ".ctor") {
					return InterestingReason.XmlSerialization;
				}
			}
			if (method.DeclaringType.FullName == "System.Xml.Serialization.XmlSerializationWriterILGen" ||
				method.DeclaringType.FullName == "System.Xml.Serialization.XmlSerializationReaderILGen") {
				if (method.Name == "GenerateMethod") {
					// this is an override of a virtual on an internal class... we don't
					// really ever care to see this show up.
					// TODO: deal with this properly.
					return InterestingReason.XmlSerialization;
				}
			}
			if (method.DeclaringType.FullName == "System.Xml.Serialization.XmlSchemaExporter") {
				if (method.Name == "ExportTypeMapping" ||
					method.Name == "ExportMembersMapping") {
					return InterestingReason.XmlSchemaMapping;
				}
			}
			return null;
		}

		// a few of the "reasons" are given special semantics wrt LinkerUnanalyzed:
		// most attributes take precedence over LinkerUnanalyzed.
		// these "big hammers" are lower precedence than LinkerUnanalyzed - so if an API
		// marked with a "big hammer" is also LinkerUnanalyzed, it will show up as
		// LinkerUnanalyzed in the output.
		bool IsBigHammer (InterestingReason reason)
		{
			return (reason.Kind == InterestingReasonKind.EventSourceBigHammer ||
					reason.Kind == InterestingReasonKind.SerializationBigHammer ||
					reason.Kind == InterestingReasonKind.KnownReflection ||
					reason.Kind == InterestingReasonKind.ToInvestigate);
		}

		InterestingReason GetInterestingReasonFromAnnotation (MethodDefinition method)
		{
			var reason = GetInterestingReasonFromAnnotationToInvestigate (method);
			if (reason != null) {
				return reason;
			}

			// 5. Lazy<T> usage of Activator.CreateInstance
			//if (method.DeclaringType.FullName == "System.Lazy`1" &&
			//	method.Name == ".ctor") {
			//	if (method.Parameters.Count == 0 ||
			//		(method.Parameters.Count == 1 && method.Parameters [0].ParameterType.FullName == "System.Boolean") ||
			//		(method.Parameters.Count == 1 && method.Parameters [0].ParameterType.FullName == "System.Threading.LazyThreadSafetyMode")) {
			//		// Lazy<T>.ctor()
			//		// Lazy<T>.ctor(bool)
			//		// Lazy<T>.ctor(LazyThreadSafetyMode)
			//		// all these ctors create Lazy<T> which will end up calling Activator.CreateInstance<T> which is currently unanalyzable.
			//		// Since we marked the internal helper which actually calls the CreateInstance as safe (to avoid reporting cases
			//		// where safe ctors are called), we need to mark the dangerous ctors as unsafe.
			//		return InterestingReason.LazyOfT;
			//	}
			//}
			if (method.DeclaringType.FullName == "System.LazyHelper" &&
				method.Name == "CreateViaDefaultConstructor") {
				// Lazy<T> has a set of ctors which create the object based on a Func, those are perfectly safe
				// as the creation of the instance is done by the specified Func (and thus analyzable by the linker).
				// The other set of ctors lead to an object which will create the instance by calling Activator.CreateInstance<T>()
				// this one is theoretically analyzable (as we should be able to deduce T), but linker currently doesn't have the ability
				// to fully analyze generics. So in the meantime these ctors will be marked as unsafe.
				// Unfortunately the switch between the two cases is based on internal variable and thus linker sees the unsafe
				// Activator.CreateInstance for all cases. So mark the internal LazyHelper.CreateViaDefaultConstructor
				// which calls the Activator.CreateInstance as safe and instead rely on the "unsafe" ctors only.
				return InterestingReason.AnnotatedLinkerFriendly;
			}


			// 1. LambdaExpression::Compile gets to Type.GetField(string)
			// while trying to emit a constants array, it gets closure constants:
			// (s_Closure_Constants = typeof(Closure).GetField(nameof(Closure.Constants)));
			// nameof() is simply ldstr in IL, so the linker doesn't know... but heuristics might be able to. currently they don't kick in for static methods.
			// let's mark this as "could be linker safe" - we could add PreserveDependency easily, or add linker heuristics.
			if (method.DeclaringType.FullName == "System.Linq.Expressions.CachedReflectionInfo") {
				if (method.Name == "get_Closure_Constants") {
					// could be made linker-friendly via PreserveDependency, or the heuristics.
					// I actually don't know why the heuristics don't catch it already.
					return InterestingReason.CachedReflectionInfo;
				}
				if (method.Name == "get_Closure_Locals") {
					return InterestingReason.CachedReflectionInfo;
				}
				// this gets rid of 4, left with:
				// 1134 stacktraces, 1130 LinkerUnanalyzed.
				// revisiting it after doing BoxStorage stuff...
				if (method.Name == "get_DateTime_MinValue") {
					return InterestingReason.CachedReflectionInfo;
				}
				if (method.Name == "get_Decimal_Zero") {
					return InterestingReason.CachedReflectionInfo;
				}
				if (method.Name.StartsWith ("get_")) {
					return InterestingReason.CachedReflectionInfo;
				}
				// after taking care of BoxStorage, and all getters on CachedReflectionInfo, we have:
				// 1193 stacktraces, 64 are CachedReflectionInfo, still 1125 LinkerUnanalyzed.
			}

			// 2. LambdaExpression::Compile, while emitting hoisted locals for a lambda, creates a storage (for the local I guess), ElementBoxStorage
			// this ctor takes a ""ParameterExpression variable"". it does typeof(StrongBox<>).MakeGenericType(variable.Type)
			// which is probably linker-unfriendly, even though the current linker doesn't warn or detect it.
			// then on this, it does GetField("Value");
			if (method.DeclaringType.FullName == "System.Linq.Expressions.Compiler.CompilerScope/ElementBoxStorage") {
				if (method.Name == ".ctor") {
					return InterestingReason.ElementBoxStorage;
					// this ctor uses typeof(StrongBox<>).MakeGenericType(passed-in parameter) and GetField("Value")
					// with this, see 390 "interesting", 1136 stacktraces (2 are ElementBoxtorage)
					// but still the lambda compilation ends up somewhere unsafe...
					// LocalBoxStorage does the same.
				}
			}
			if (method.DeclaringType.FullName == "System.Linq.Expressions.Compiler.CompilerScope/LocalBoxStorage") {
				if (method.Name == ".ctor") {
					return InterestingReason.LocalBoxStorage;
				}
			}

			// 3. DispatchProxy::Create gets to ProxyBuilder::AddMethodImpl, which does typeof(Type).GetRuntimeMethod("GetTypeFromHandle")
			// to il emit a typeof() call. Type.GetTypeFromHandle is *probably* always kept... but maybe we shouldn't assume that.
			// could be fixed via a heuristic pretty easily.
			// if this method calls anything else that's unsafe, we might be in trouble still.
			if (method.DeclaringType.FullName == "System.Reflection.DispatchProxyGenerator/ProxyBuilder") {
				if (method.Name == "AddMethodImpl") {
					return InterestingReason.DispatchProxy;
				}
			}

			// 4. TimeSpanConverter::ConvertTo does typeof(TimeSpan).GetMethod(nameof(TimeSpan.Parse) - the linker should be able to detect this pretty simply as well.
			if (method.DeclaringType.FullName == "System.ComponentModel.TimeSpanConverter") {
				if (method.Name == "ConvertTo") {
					return InterestingReason.TypeConverterConvertToSimple;
					// this one no longer shows up in the output because it is itself a public API.
				}
			}

			// 5. XmlSerializer::FromMappings ref emits a serialization contract assembly or something.
			// GeneratePublicMethods does typeof(Hashtable).GetMethod("set_Item") - which could probably be detected by the linker.
			if (method.DeclaringType.FullName == "System.Xml.Serialization.XmlSerializationILGen") {
				if (method.Name == "GeneratePublicMethods") {
					return InterestingReason.XmlSerializationILGenSimple;
				}
			}

			// 6. Lambda compilation does CompileInvocationExpression(Expression expr)
			// which does GetMethod("Compile") on a Type that comes from the expr. the linker doesn't understand that.
			if (method.DeclaringType.FullName == "System.Linq.Expressions.Interpreter.LightCompiler") {
				if (method.Name == "CompileInvocationExpression") {
					return InterestingReason.LinqCompileReflection;
				}
			}

			// 7. XmlSchemaExporter::ExportTypeMapping gets to XmlSchemaExporter::ExportDefaultValue
			// does formatter = typeof(XmlConvert), then formatter.GetMethod("ToString");
			// this could be rewritten and the linker made to understand it.
			if (method.DeclaringType.FullName == "System.Xml.Serialization.XmlSchemaExporter") {
				if (method.Name == "ExportDefaultValue") {
					return InterestingReason.ExportDefaultValue;
				}
			}

			// 8. lambda compilation does EmitInvocationExpression, which gets a type from a passed-in Expression, and does GetMethod("Compile")
			if (method.DeclaringType.FullName == "System.Linq.Expressions.Compiler.LambdaCompiler") {
				if (method.Name == "EmitInvocationExpression") {
					return InterestingReason.LinqCompileReflection;
				}
			}

			// 9. XmlSerializer::FromMappings does XmlSerializationWriterILGen, generates a ref emit assembly
			// which calls into SourceInfo ctor...
			// shows up in stacktrace as .ctor -> .cctor, which might be wrong
			// but in any case, the cctor does return typeof(IList).GetMethod("get_Item") which could be detected
			// the static member is a Lazy<MethodInfo>, so it's a generated lambda that we need to mark.
			if (method.DeclaringType.FullName == "System.Xml.Serialization.SourceInfo/<>c") {
				if (method.Name == "<.cctor>b__20_0") {
					return InterestingReason.XmlSerializationSourceInfoSimple;
				}
			}

			// 10. XmlSerializater::FromMappings uses XmlSerializationReaderILGen. while it's writing things, does ILGenParamsReadSource, which does
			// CodeGenerator::StoreArrayElement. this ends up doing typeof(Array).GetMethod("SetValue") - we should be able to detect it.
			if (method.DeclaringType.FullName == "System.Xml.Serialization.CodeGenerator") {
				if (method.Name == "StoreArrayElement") {
					return InterestingReason.XmlSerializationCodeGeneratorSimple;
				}
			}

			// 14. ExpandoTryDeleteValue does ExpandoObject cctor, which does GetMethod(string)
			// this method is obsolete, and we could just mark it unsafe.
			// but these particular statics look like: typeof(RuntimeOps).GetMethod(nameof(RuntimeOps.ExpandoTrySetValue));
			// which would be detectable, so should be safe
			if (method.DeclaringType.FullName == "System.Dynamic.ExpandoObject") {
				if (method.Name == ".cctor") {
					return InterestingReason.ExpandoObjectSimple;
				}
			}

			// 15. CallSiteHelpers compares a MethodBase's type against that of a known static method, to see if the input was dynamic.
			// the static method is typeof(object).GetMethod(nameof(ToString)) - should be easy to detect.
			if (method.DeclaringType.FullName == "System.Runtime.CompilerServices.CallSiteHelpers") {
				if (method.Name == ".cctor") {
					return InterestingReason.CallSiteHelpersSimple;
				}
			}

			// 16. more about lambda compilation:
			// compiling a lambda, creating a delegate does DelegateHelpers.cctor
			// typeof(Func<object[], object>).GetMethod("Invoke");
			// this looks detectable - the ldtoken has Func<object[], object> in the IL so should be understandable like other simple patterns
			// private static readonly MethodInfo s_ArrayEmpty = typeof(Array).GetMethod(nameof(Array.Empty)).MakeGenericMethod(typeof(object));
			// this might be harder to understand. however, Array and Array.Empty should exist
			// as would object. so really, this should be safe.
			if (method.DeclaringType.FullName == "System.Dynamic.Utils.DelegateHelpers") {
				if (method.Name == ".cctor") {
					return InterestingReason.DelegateHelpersStatics;
				}
			}

			// 17. serialization moved to reflectionapis

			// 18. public TypeConverter ConvertTo implementation on ExtendedProtectionPolicyTypeConverter
			// does typeof(ExtendedProtectionPolicy).GetConstructor(parameterTypes)
			// might be hard to detect.
			if (method.DeclaringType.FullName == "System.Security.Authentication.ExtendedProtection.ExtendedProtectionPolicyTypeConverter" ||
				// there are two PointConverters, both behave similarly
				method.DeclaringType.FullName == "System.Drawing.PointConverter" ||
				method.DeclaringType.FullName == "System.Drawing.RectangleConverter" || // similar...
				method.DeclaringType.FullName == "System.Drawing.SizeConverter" || // ...
				method.DeclaringType.FullName == "System.Drawing.SizeFConverter" ||
				method.DeclaringType.FullName == "System.ComponentModel.DateTimeConverter" ||
				method.DeclaringType.FullName == "System.ComponentModel.DateTimeOffsetConverter" ||
				method.DeclaringType.FullName == "System.ComponentModel.DecimalConverter" ||
				method.DeclaringType.FullName == "System.ComponentModel.GuidConverter" ||
				method.DeclaringType.FullName == "System.ComponentModel.CultureInfoConverter" ||
				method.DeclaringType.FullName == "System.ComponentModel.VersionConverter" ||
				method.DeclaringType.FullName == "System.UriTypeConverter") {
				if (method.Name == "ConvertTo") {
					return InterestingReason.TypeConverterConvertToComplex;
				}
			}
			// now have 1036 LinkerUnanalyzed.

			// 19. XmlSerializer::FromMappings does TempAssembly ctor, to generaterefemitassembly (similar to above).
			// generating the ref emit assembly does Type.GetConstructor (complex-ish) and SetCustomAttribute...
			// this also calls into lots of other methods that the linker doesn't understand... may just need to disable it entirely.
			// typeof(AssemblyVersionAttribute).GetConstructor(new Type[] { typeof(string) });
			if (method.DeclaringType.FullName == "System.Xml.Serialization.TempAssembly") {
				if (method.Name == "GenerateRefEmitAssembly") {
					return InterestingReason.GenerateRefEmitAssembly;
				}
			}

			// 20. Activator.CreateInstance initializes a cache of some kind, calls into Type.GetConstructor:
			// typeof(CtorDelegate).GetConstructor(new Type[] { typeof(object), typeof(IntPtr) })!;
			// could maybe be detected. but do we want to? Activator.CreateInstance itself is unsafe! I think this is
			// the overload CreateInstance<T>(). calls CreateInstanceDefaultCtor()
			// CreateInstance<T>() could be understood in principle (if we understand how T flows statically).
			// so that API should be handled separately from the call to GetConstructor().
			// after it gets the ctor of the CtorDelegate, it invokes it passing a function pointer for the method handle
			// that comes from the ActivatorCache ctor. was initialized by native code, with InternalCall
			// RuntimeHandles.cs: RuntimeTypeHandle.CreateInstance. that one is dangerous.
			// but the linker won't see that.
			// TODO: mark RuntimeTypeHandle.CreateInstance as dangerous.
			if (method.DeclaringType.FullName == "System.RuntimeType/ActivatorCache") {
				if (method.Name == "Initialize") {
					return InterestingReason.ActivatorCacheSimple;
				}
			}

			// 21. compiling lambda, EmitNewHoistedLocals, related to 2.
			// stashes the locals variable info in an instance field, does GetConstructor on a generated generic type (2. above)
			// with parameters matching the local parameterexpression's type. not easily analyzed.
			if (method.DeclaringType.FullName == "System.Linq.Expressions.Compiler.CompilerScope") {
				if (method.Name == "EmitNewHoistedLocals") {
					return InterestingReason.EmitNewHoistedLocals;
				}
			}

			// 22. moved to ReflectionApis.cs Activator and AppDomain, CreateInstance and CreateInstanceAndUnwrap
			// 22a: AppDomain has CreateInstance methods that pass it to Activator. included in the above logic for 22.
			// 22b: AppDomain has CreateInstanceAndUnwrap, which call into the above. also included in 22.

			// 23. lambda compilation EmitUnary does Type.GetConstructor, with param type computed from a passed in node
			// not supported. we should probably just disable lambda compilation at this point.
			// I don't think there are any reasonable subsets of lambda compilation that are actually supported...
			// unless we can be sure that all types are passed in with static references.
			// I think we need cross-method data flow for types at least.
			if (method.DeclaringType.FullName == "System.Linq.Expressions.Compiler.LambdaCompiler") {
				if (method.Name == "EmitUnary" ||
					method.Name == "EmitLift") {
					return InterestingReason.LinqCompileReflection;
				}
			}

			// 24. DefaultValueAttribute can be used on a property to specify default value.
			// one of the ctors takes a Type. this ctor uses reflection to get TypeDescriptor::ConvertFromInvariantString from System.ComponentModel.TypeConverter
			// and calls this delegate on the input type specified in the ctor.
			// most likely the ctor will have a typeof()...
			// might be solved by a simple PreserveDependency.
			if (method.DeclaringType.FullName == "System.ComponentModel.DefaultValueAttribute") {
				if (method.Name == ".ctor" && method.Parameters.Count == 2) {
					return InterestingReason.DefaultValueAttribute;
				}
			}

			// 25. AppDomain.GetThreadPrincipal uses reflection to load GenericPrincipal::GetDefaultInstance from System.Security.Claims.dll.
			// assume this isn't supported? or if it is need a preservedependency attribute.
			if (method.DeclaringType.FullName == "System.AppDomain") {
				if (method.Name == "GetThreadPrincipal") {
					return InterestingReason.ThreadPrincipal;
				}
			}
			// the above is internal, this is the only public API that gets there.
			if (method.DeclaringType.FullName == "System.Threading.Thread") {
				if (method.Name == "get_CurrentPrincipal") {
					return InterestingReason.ThreadPrincipal;
				}
			}

			// 26. Marshal.OffsetOf moved to reflectionapis

			// 27. TODO: DEDUP ResourceSet
			if (method.DeclaringType.FullName == "System.Resources.ResourceManager") {
				switch (method.Name) {
					case "GetSatelliteContractVersion":
						// This calls Assembly.GetCustomAttribute<SatelliteContractVersionAttribute> - which is potentially unsafe
						// but SatelliteContractVersionAttribute is sealed, so the linker will mark it and keep its instances
						// and there's no chance of a derived instance anywhere - so this is linker friendly.
						return InterestingReason.AnnotatedLinkerFriendly;
				}
			}
			if (method.DeclaringType.FullName == "System.Resources.ManifestBasedResourceGroveler") {
				switch (method.Name) {
					case "CreateResourceSet":
						// 1. CreateResourceSet calls activator.createinstance on a potentially custom resource set type
						// disable the custom resource set API, and mark CreateResourceSet as safe.
						// this changes one stacktrace to AnnotatedLinkerFriendly.
						// this API uses CreateInstance on a ResourceSet type passed in from the outside.
						// we mark the ctor that passes a ResourceSet as unsafe, so it should never be used, making this API safe.
						return InterestingReason.AnnotatedLinkerFriendly;
					case "GetNeutralResourcesLanguage":
						// This calls Assembly.GetCustomAttribute<NeutralResourcesLanguageAttribute> - which is potentially unsafe
						// but NeutralResourcesLanguageAttribute is sealed, so the linker will mark it and keep its instances
						// and there's no chance of a derived instance anywhere - so this is linker friendly.
						return InterestingReason.AnnotatedLinkerFriendly;
				}
			}
			// TODO: what about FileBasedResourceGroveler?
			// if (method.DeclaringType.FullName == "System.Resources.FileBasedResourceGroveler" &&
			//     method.Name == "") {
			//     return InterestingReason.
			// }
			// the ResourceManager ctor is moved out to ReflectionApis.cs.

			// 28. ModuleBuilder : Module, implements protected virtual GetMethodImpl(string name).
			// calls GetMethodInternal(string name), which does RuntimeType.GetMethod(name)
			// these aren't safe.
			if (method.DeclaringType.FullName == "System.Reflection.RuntimeModule" &&
				method.Name == "GetMethodInternal") {
				return InterestingReason.GetMethod;
			}
			// GetMethodImpl check is moved to reflectionapis.

			// 29. TypeBuilder::DefineDefaultConstructor moved to reflectionapis.cs

			// 30. EventSource::GetGuid does GetCustomAttributeHelper(type, typeof(EventSourceAttribute))
			// and EventSource uses this pattern in a few places
			// the helper exists for the case where we might be reflecting over the ReflectionOnly load context.
			// (I thought this didn't exist in core?). in that case, the custom assemblies must be built by hand.???
			// the helper goes GetCustomAttribute(attributeType), sometimes Activator.CreateInstance(attributeType)
			// maybe this could be rewritten?
			// maybe we can be sure that the attribute is always kept since the linker will see typeof(Attribute)?
			if (method.DeclaringType.FullName == "System.Diagnostics.Tracing.EventSource") {
				if (method.Name == "GetCustomAttributeHelper") {
					return InterestingReason.EventSourceGetCustomAttributeHelperLooksSafe;
				}
			}
			// disable eventsource with a big hammer.
			// mark EventSource as unsafe by simulating attributes on public surface area
			if (method.DeclaringType.FullName == "System.Diagnostics.Tracing.EventSource" &&
				method.Name == "Initialize") {
				return InterestingReason.EventSourceBigHammer;
			}
			if (method.DeclaringType.FullName == "System.Diagnostics.Tracing.EventSource" &&
				method.Name == ".ctor") {
				return InterestingReason.EventSourceBigHammer;
			}
			if (method.DeclaringType.FullName == "System.Diagnostics.Tracing.EventSource" &&
				method.Name == "SendCommand") {
				///
			}
			// use a big hammer to get every call to a method on EventSource.
			if (method.DeclaringType.FullName == "System.Diagnostics.Tracing.EventSource") {
				return InterestingReason.EventSourceBigHammer;
			}


			// 31. ComponentActivator does LoadAssemblyAndGetFunctionPointer()
			// called from hostpolicy, comhost I think?
			// takes native intptr with assembly name, type name, method name... definitely unsafe.
			// just mark the public entry as unsafe.
			if (method.DeclaringType.FullName == "Internal.Runtime.InteropServices.ComponentActivator") {
				if (method.Name == "LoadAssemblyAndGetFunctionPointer") {
					return InterestingReason.ComponentActivator;
				}
			}

			// 32. generating a linq expression Call to a method by name
			// tries to find that method on a passed-in Type using a helper method. seems unsafe...
			// after finding the method, calls into a factory method taking a MethodInfo.
			// the linker tries to detect such calls, but it doesn't detect the inner call.
			// could in principle have the linker understand simple callsites of the one that takes a string.
			if (method.DeclaringType.FullName == "System.Linq.Expressions.Expression") {
				if (method.Name == "Call" &&
					method.Parameters.Count == 4 &&
					method.Parameters [1].ParameterType.FullName == "System.String") {
					return InterestingReason.LinqExpressionCallString;
				}
			}

			// 33. lambda expression compilation tries to generate a method call expression
			// with a methodinfo type paed in from an expression node.
			// passing around an expression that maintains a MethodInfo...
			// might be safe to generate code. but I don't know where the MethodInfo comes from.
			if (method.DeclaringType.FullName == "System.Linq.Expressions.Interpreter.LightCompiler") {
				if (method.Name == "CompileCoalesceBinaryExpression") {
					return InterestingReason.LinqCompileReflection;
				}
			}

			// 34. public TypeConverter property getter of PropertyDescriptor
			// MemberDescriptor maintains an attribute array (passed in at construction)
			// to get the TypeConverter, first get the attributes, and look for one that is TypeConverterAttribute
			// gets the typeconverter from the name stored in the attribute,
			// and calls a CreateInstance helper on that type (TypeDescriptor.CreateInstance)
			// which, if it was a TypeDescriptionProvider, does TypeDescriptionProvider.CreateInstance
			// otherwise NodeFor(objectType).CreateInstance
			// anyway, it looks at attributes on the PropertyDescriptor, finds those with TypeConverter,
			// gets the TypeConverter type,
			// then creates a new TypeConverter and returns it
			// could be unsafe if TypeConverterAttribute is done by string.
			// by type, would be fine as long as linker understands this.... actually no.
			// that doesn't teach it to keep the ctor alive.
			// could maybe fix this by marking the TypeConverter attribute with string as unsafe,
			// and teaching linker to keep ctors? or even teaching the linker about string attribute.
			if (method.DeclaringType.FullName == "System.ComponentModel.PropertyDescriptor") {
				if (method.Name == "get_Converter" ||
					method.Name == "GetEditor") {
					// 34a. similar to above, GetEditor(Type) looks for [EditorAttribute], gets type by name
					// the attribute always stores a type name, and retrieves it by name as well.
					return InterestingReason.ComponentModelAttributeRequiresCtor;
				}
			}

			// 35. componentmodel attribute collection maintains mapping of Type attributeType -> Attribute
			// in some cases, gets default value of an attribute type as fallback.
			// this gets the attribute type via reflection and gets the "Default" static field.
			// would need to keep the Default field.
			if (method.DeclaringType.FullName == "System.ComponentModel.AttributeCollection") {
				if (method.Name == "GetDefaultAttribute") {
					return InterestingReason.ComponentModelAttributeRequiresDefaultField;
				}
			}

			// 36. componentmodel helper that looks for method by name.
			// we might need to understand callers of this if it is common.
			if (method.DeclaringType.FullName == "System.ComponentModel.MemberDescriptor") {
				if (method.Name == "FindMethod") {
					return InterestingReason.ComponentModelFindMethod;
				}
			}

			// 37. componentmodel LicenseProvider attribute gets a type in the ctor
			// possibly by name. getting the LicenseProvider type out of it requires it to exist
			if (method.DeclaringType.FullName == "System.ComponentModel.LicenseProviderAttribute") {
				if (method.Name == "get_LicenseProvider") {
					return InterestingReason.ComponentModelAttributeRequiresType;
				}
			}

			// 38. PropertyTabAttribute maintains list of tab classes (afaik only will ever have length 1?)
			// possibly by name, in which case it does GetType.
			// it possibly does Assembly.Load for the assembly name given as part of type name.
			if (method.DeclaringType.FullName == "System.ComponentModel.PropertyTabAttribute") {
				if (method.Name == "get_TabClasses") {
					return InterestingReason.ComponentModelAttributeRequiresType;
				}
			}

			// 39. ComAwareEventInfo moved to ReflectionApis.

			// 40. crypto config: CreateFromName supports configuring crypto algorithms by name.
			// supports adding algorithms (AddAlgorithm), and a default lookup table
			// mapping strings, some to a type referenced via typeof, and some to a string specifying an assembly-qualified type name
			// for string case, does GetType. also falls back to GetType of arbitrary string if it wasn't in the tble
			// if the type was found, get all ctors, look for one with matching parameters
			// bind to a method as ConstructorInfo, and invoke the ctor.
			if (method.DeclaringType.FullName == "System.Security.Cryptography.CryptoConfig") {
				if (method.Name == "CreateFromName") {
					return InterestingReason.CryptoConfig;
				}
			}

			// 41. linq generating a Coalesce expression, passes delegate type (from input LambdaExpression)
			// into a helper that gets the "Invoke" method via reflection
			// might be preserved for lambdas always?
			if (method.DeclaringType.FullName == "System.Dynamic.Utils.TypeUtils") {
				if (method.Name == "GetInvokeMethod") {
					return InterestingReason.LinqCompileReflection;
				}
			}

			// 42. linq IndexExpression update does MakeIndex, which calls Property()... that linker detects as unanalyzed
			// linq generating a property
			if (method.DeclaringType.FullName == "System.Linq.Expressions.Expression") {
				if (method.Name == "MakeIndex") {
					return InterestingReason.LinqCompileReflection;
				}
			}

			// 43.linq creating a Lambad expression. calls CreateLambda(Type delegateType)
			// which makes a generic Expression<delegateType>, gets its "Create" or "CreateExpressionFunc" method
			// maybe this method is always kept alive for delegates anyway? don't know. mark as unsafe for now.
			if (method.DeclaringType.FullName == "System.Linq.Expressions.Expression") {
				if (method.Name == "CreateLambda") {
					return InterestingReason.LinqCompileReflection;
				}
			}

			// 44. linq make a member access given a PropertyInfo. should be fine but linker warns.
			if (method.DeclaringType.FullName == "System.Linq.Expressions.Expression") {
				if (method.Name == "MakeMemberAccess") {
					return InterestingReason.LinkerShouldNotWarn;
				}
				if (method.Name == "Call") {
					if (method.Parameters.Count == 3 &&
						method.Parameters [1].ParameterType.FullName == "System.Reflection.MethodInfo") {
						return InterestingReason.LinkerShouldNotWarn;
					}
				}
			}

			// 45. data column type has DataType setter, with a TypeConverter(typeof(ColumnTypeConverter))]
			// and a PreserveDependency (to work around linker's lack of knowledge of TypeConverter on property)
			// but latest linker should have a fix. to keep ColumnTypeConverter ctor
			// the setter calls DefaultValue getter (also has similar dependency, and type converter)
			// which does _dataType.GetProperty("Null") if it implements nullable.
			// so should keep Null property of a DataColumn's _dataType.
			if (method.DeclaringType.FullName == "System.Data.DataColumn") {
				if (method.Name == "get_DefaultValue") {
					return InterestingReason.DataColumnGetsNullPropertyOfType;
				}
			}

			// 46. db provider factory gets provider type from string
			if (method.DeclaringType.FullName == "System.Data.Common.DbProviderFactories") {
				if (method.Name == "GetProviderTypeFromTypeName") {
					return InterestingReason.DbProviderType;
				}
			}

			// 47. dbprovider gets factory from DataRow. from the row, it gets the column matching "AssemblyQualifiedName"
			// which it uses to get provider type from typename (handled above).
			// but even if that were safe, it ends up using the provider type to do GetField(InstanceFieldName)
			// which is just "Instance"... but linker doesn't know which type the field is on. unsafe.
			if (method.DeclaringType.FullName == "System.Data.Common.DbProviderFactories") {
				if (method.Name == "GetFactoryInstance") {
					return InterestingReason.DbProviderType;
				}
			}

			// 48. componentmodel annotations has a LocalizableString class.
			// GetLocalizableValue gets the localized string. holds a resource name. this looks up the name in the resource type (instance 
			// if the resource type is null, this is fine. if it's not null, it does _resourceType.GetRuntimeProperty(string)
			// to get resource value via reflection. linker can't know.
			// this is actually safe if there's no _resourceType. disable the ResourceType setter. and mark it safe.
			if (method.DeclaringType.FullName == "System.ComponentModel.DataAnnotations.LocalizableString") {
				if (method.Name == "GetLocalizableValue") {
					return InterestingReason.AnnotatedLinkerFriendly;
				}
				if (method.Name == "set_ResourceType") {
					return InterestingReason.ResourceTypeReflection;
				}
			}

			// 49. componentmodel annotations has a Max/Minlength attribute. IsValid method takes arbitrary object
			// and checks by getting the object's Count property via reflection... linker-unsafe.
			if (method.DeclaringType.FullName == "System.ComponentModel.DataAnnotations.CountPropertyHelper") {
				if (method.Name == "TryGetCount") {
					return InterestingReason.ComponentValidatorReflection;
				}
			}

			// 50. JsonConverterAttribute ctor takes a Type (or can have overridden CreateConverter method).
			// this is used in json serialization - either call the CreateConverter method,
			// or do CreateInstance of the type, so its ctor must be preserved.
			// could be fixed by keeping ctors for all JsonConverterAttribute(typeof(Foo))
			if (method.DeclaringType.FullName == "System.Text.Json.JsonSerializerOptions") {
				if (method.Name == "GetConverterFromAttribute") {
					return InterestingReason.JsonConverterAttributeRequiresCtor;
				}
			}

			// 51. EarlyBoundInfo (internal) ctor takes Type, does GetConstructor. type is passed in via some helper.
			// not safe (unless we understand all the ways a type can get there)
			if (method.DeclaringType.FullName == "System.Xml.Xsl.Runtime.EarlyBoundInfo") {
				if (method.Name == ".ctor") {
					return InterestingReason.EarlyBoundInfoRequiresCtor;
				}
			}

			// 52. xml serialization uses a helper to get method from type. type and method name come various caches, readers, etc.
			if (method.DeclaringType.FullName == "System.Xml.Serialization.TempAssembly") {
				if (method.Name == "GetMethodFromType") {
					return InterestingReason.XmlSerializationGetMethodFromType;
				}
			}

			// 53. Attribute has a helper GetParentDefinition, used to walk parent chain.
			// in some cases looks like attributes inherit getters and setters of the parent.
			// it checks GetProperty(string) of the parent to get the property with same name on the parent.
			// not sure if the linker can remove this.
			if (method.DeclaringType.FullName == "System.Attribute") {
				if (method.Name == "GetParentDefinition") {
					if (method.Parameters.Count == 2 && method.Parameters [0].ParameterType.FullName == "System.Reflection.PropertyInfo") {
						return InterestingReason.AttributeParentReflection;
					}
					if (method.Parameters.Count == 1 && method.Parameters [0].ParameterType.FullName == "System.Reflection.EventInfo") {
						return InterestingReason.AttributeParentReflection;
					}
				}
			}

			// 54. typebuilder getcustomattributes does addcustomattributes
			// similar: GetCustomAttributes walks parent chain, accumulates parent's custom attributes.
			// for named args in the attribute, it iterates through to look for property or field data
			// and then does GetType() of the data, and does GetProperty, GetSetMethod, Invoke
			// I guess attributes have some kind of behavior where named params to constructor get stashed in
			// fields? and getting the customattribute will actually do the assignment, via reflection,
			// so linker would need to preserve these fields/getters/setters of custom attributes with named params
			if (method.DeclaringType.FullName == "System.Reflection.CustomAttribute") {
				if (method.Name == "AddCustomAttributes") {
					return InterestingReason.AttributeNamedParamReflection;
				}
				// CustomAttributes.AddCustomAttributes is the implementation of the creation of Attribute class instances
				// from serialized data in the metadata. Part of its implementation walks the list of serialized Property=Value and Field=Value
				// pairs and applies the values to the new instance. It does this by getting PropertyInfo/FieldInfo and setting the value
				// through reflection. So for this it calls Type.GetProperty/Type.GetField and gets marked.
				// This means that for it to work correctly the attribute must have all its properties and fields persisted. That's exactly
				// what linker does. If linker marks certain attribute to be kept, all of its fields and properties are kept as well,
				// always.
				// The only way this could break is if there's a type/method/item which linker doesn't know about which has an attribute
				// not seen anywhere else. In that case it's the responsibility of the callsite which references this unrecognized type/method/item
				// to make sure that all necessary attributes are also preserved.

			}

			// 55. TODO: Dedup
			if (method.DeclaringType.FullName == "System.Diagnostics.StackFrameHelper" &&
				method.Name == "InitializeSourceInfo") {
				// this method retrieves a SourceInfo assembly and methods to get source lines via reflection
				// perhaps could be solved via PreserveDependencyAttribute if we want to always keep it.
				return InterestingReason.SourceInfo;
			}
			// loads System.Diagnostics.Stacktrace.dll via reflection for source info
			//    gets StackTraceSymbols::GetSourceLineInfo, and calls it on each frame.
			// options:
			//   mark it as linker-safe (not without ensuring the linker doesn't remove GetSourceLineInfo if it will be used at runtime)
			//   add a PreserveDependency attribute to tell the linker to keep it.
			//   disable this feature when the linker is used... no - the user has no control over whether it's used.
			// let's explicitly mark it as unsafe for now, as something to deal with.
			// this also changes one stacktrace.

			// 56. componentmodel typedescriptor CheckDefaultProvider(Type)
			// check for a default type descriptor provider attribute. does type.GetCustomAttributes(typeof(TypeDescriptionProviderAttribute)))
			// takes the provider attribute, which has a TypeName, and does Type.GetType(by name), and CreateInstance()
			if (method.DeclaringType.FullName == "System.ComponentModel.TypeDescriptor") {
				if (method.Name == "CheckDefaultProvider") {
					return InterestingReason.ComponentModelAttributeRequiresCtor;
				}
			}

			// 57. BindingList<T> getter does typeof(T), GetConstructor to check if it has default ctor
			// can take user-supplied new item (I think via OnAddingNew?)
			// or if not supplied, will try to call the ctor.
			if (method.DeclaringType.FullName == "System.ComponentModel.BindingList`1") {
				if (method.Name == "get_ItemTypeHasDefaultConstructor") {
					return InterestingReason.ComponentModelBindingListRequiresCtor;
				}
			}

			// 58. vb IDOBinder
			// create ref callsite and invoke... does GetType(Object), later creates a callsite
			// and on it does GetType().GetField("Target") to get Target of callsite.
			if (method.DeclaringType.FullName == "Microsoft.VisualBasic.CompilerServices.IDOUtils") {
				if (method.Name == "CreateRefCallSiteAndInvoke" ||
					method.Name == "CreateFuncCallSiteAndInvoke" ||
					method.Name == "CreateConvertCallSiteAndInvoke") {
					return InterestingReason.IDOBinderReflection;
				}
			}

			// 59. serialization moved to reflectionapis

			// 60. binding a callsite, Stitch<T>:
			// producing a new rule (delegate?) does Stitch(Expression binding, LambdaSignature<T> signature)
			// does typeof(CallSite<T>).GetProperty(nameof(CallSite<T>.Update))
			// might be ok
			if (method.DeclaringType.FullName == "System.Runtime.CompilerServices.CallSiteBinder") {
				if (method.Name == "Stitch") {
					return InterestingReason.CallSiteBinderSimple;
				}
			}

			// 61. GetAnyStaticMethodValidated
			// is a linq util method, wraps type.GetMethod(name), passed-in type and name, with checking of arg types
			// used by GetBooleanOperator (similar, wrapper), and others
			if (method.DeclaringType.FullName == "System.Dynamic.Utils.TypeExtensions") {
				if (method.Name == "GetAnyStaticMethodValidated") {
					return InterestingReason.LinqGetMethodWrapper;
				}
			}

			// 62. CheckMethod(MethodInfo, MethodInfo) checks that they match.
			// used to get a PropertyInfo from a MethodInfo (by getting a type's properties, iterating, and doing CheckMethod against a passed-in MethodInfo)
			// to CheckMethod, if the declaring type is an interface, must match by name instead of reference equality.
			// so does type.GetMethod(method.Name)
			// might be ok? since there would already be a MethodInfo for the relevant one.
			if (method.DeclaringType.FullName == "System.Linq.Expressions.Expression") {
				if (method.Name == "CheckMethod") {
					return InterestingReason.LinqCheckMethod;
				}
			}

			// 63. deserializing a data table schema (columns)
			// gets a type name from column index, does Type.GetType()
			// could probably be rewritten to avoid reflection? is the type guaranteed to exist?
			if (method.DeclaringType.FullName == "System.Data.DataTable") {
				if (method.Name == "DeserializeTableSchema") {
					return InterestingReason.DeserializeDataTableGetType;
				}
			}

			// 64. xml serialization getting element type of enumerator (GetEnumeratorElementType)
			// does type.GetMethod("GetEnumerator"), and type.GetMember("...IEnumerable<*"")
			// and enumerator.ReturnType.GetProperty("Current"")
			// .. type.GetMethod("Add")
			// may be ok? if the interface implementation is kept
			if (method.DeclaringType.FullName == "System.Xml.Serialization.TypeScope") {
				if (method.Name == "GetEnumeratorElementType") {
					return InterestingReason.GetEnumeratorElementType;
				}
			}

			// 65. CryptoConfigForwarder (used when creating various crypto algorithms from name)
			// static cctor does BindCreateFromName, with a type name as string.
			// uses reflection to get CryptoConfig.CreateFromName. not local, but should be safe if that dependency is kept.
			if (method.DeclaringType.FullName == "System.Security.Cryptography.CryptoConfigForwarder") {
				if (method.Name == "BindCreateFromName") {
					return InterestingReason.CryptoConfigForwarderReflectionDependency;
				}
			}

			// 66. nested method in SqlUdtStorage, GetStaticNullForUdtType.
			// GetStaticNullForUdtType gets a Type passed in (via SqlUdtStorage ctor usually)
			// does type.GetProperty("Null"), type.GetField("Null")
			if (method.DeclaringType.FullName == "System.Data.Common.SqlUdtStorage/<>c__DisplayClass6_0") {
				if (method.Name == "<GetStaticNullForUdtType>b__0") {
					return InterestingReason.SqlUdtStorageStaticNull;
				}
			}

			// 67. XslCompiledTransForm::Load, compiles Qil to MSIL. XmlILGenerator does BakeMethods into an XmlILModule
			// which does _typeBldr.CreateTypeInfo().AsType()
			// does GetMethod(methName), where name comes from _methods on the XmlILModule.
			// not analyzable unless higher-level patterns are understood.
			if (method.DeclaringType.FullName == "System.Xml.Xsl.IlGen.XmlILModule") {
				if (method.Name == "BakeMethods") {
					return InterestingReason.XmlILGeneratorBakeMethodsReflection;
				}
			}

			// 68. Xml serialization does GetCollectionElementType, does GetDefaultIndexer(Type, string)
			// this does type.GetDefaultMembers(), iterates through and casts each into a PropertyInfo then a MethodInfo.
			// then method.GetParameters(), and checks parameter types.
			// later, gets the "Add" method of the type, throws exception if not found.
			// oddly, the memberInfo is not actually used except to throw an exception.
			if (method.DeclaringType.FullName == "System.Xml.Serialization.TypeScope") {
				if (method.Name == "GetDefaultIndexer") {
					return InterestingReason.XmlSerializationRequiresAddMethod;
				}
			}

			// 69. GetMethodFromSchemaProvider for xml reflection importer
			// gets a type, and does GetMethod(provider.MethodName).
			// an ExtensionMethod. internally does GetMethod().
			if (method.DeclaringType.FullName == "System.Xml.Extensions.ExtensionMethods") {
				if (method.Name == "GetMethod") {
					return InterestingReason.GetMethod;
				}
			}
			if (method.DeclaringType.FullName == "System.Xml.Serialization.XmlReflectionImporter") {
				if (method.Name == "GetMethodFromSchemaProvider") {
					return InterestingReason.XmlReflectionGetMethod;
				}
			}

			// 70. xml serialization getconstructorflags does GetConstructor extension method
			// type gets passed in through a few methods at least
			if (method.DeclaringType.FullName == "System.Xml.Extensions.ExtensionMethods") {
				if (method.Name == "GetConstructor") {
					return InterestingReason.GetConstructor;
				}
			}
			if (method.DeclaringType.FullName == "System.Xml.Serialization.TypeScope") {
				if (method.Name == "GetConstructorFlags") {
					return InterestingReason.XmlSerializationGetConstructorFlags;
				}
			}

			// 71. XMLSchema, loading a schema does HandleRefTableProperties, which does SetProperties
			// takes UnhandledAttributes specified in XmlSchemaElement
			// each attribute specifies a LocalName. except for a few that are skipped, it does
			// GetProperties(instance)[name] to get PropertyDescriptor with specified name.
			// then the PropertyType is used to get a TypeConverter (XMLSchema.GetConverter(type)
			// and uses that to convert the value string (specified by attribute) into a property value.
			// if it can't convert from string, and the type was Type, then it does Type.GetType(value) -value specified by the attribute
			// finally, sets the value of this property on instance to the property value.
			if (method.DeclaringType.FullName == "System.Data.XSDSchema") {
				if (method.Name == "SetProperties") {
					return InterestingReason.XMLSchemaSetPropertiesGetType;
				}
			}

			// 72. CreateCustomNoMatchDelegate for callsite binding does typeof(CallSiteOps).GetMethod(nameof(CallSiteOps.SetNotMatched)
			// should be detectable.
			if (method.DeclaringType.FullName == "System.Runtime.CompilerServices.CallSite`1") {
				if (method.Name == "CreateCustomNoMatchDelegate") {
					return InterestingReason.CallSiteSimple;
				}
			}

			// 73. emitting dynamic expression for linq, does siteType.GetField("Target")
			// where siteType comes from expression, CreateCalleSite().GetType()
			if (method.DeclaringType.FullName == "System.Linq.Expressions.Compiler.LambdaCompiler") {
				if (method.Name == "EmitDynamicExpression") {
					return InterestingReason.LinqCompileReflection;
				}
			}

			// 74. DataStorage has GetType wrapper
			if (method.DeclaringType.FullName == "System.Data.Common.DataStorage") {
				if (method.Name == "GetType") {
					return InterestingReason.DataStorageGetType;
				}
			}

			// 75. serialization moved to reflectionapis

			// 76. runtime binder loads symbols from type. AddAggregateToSymbolTable
			// the passed-in type is used to get all kinds of information, basetype, etc...
			// generic type definition, and eventually GetConstructor, just to see if it has a public no-arg constructor,
			// which is saved as a boolean flag.
			if (method.DeclaringType.FullName == "Microsoft.CSharp.RuntimeBinder.SymbolTable") {
				if (method.Name == "AddAggregateToSymbolTable") {
					return InterestingReason.RuntimeBinderReflection;
				}
			}

			// 77. linq emit array construction code, does arrayType (passed-in) GetConstructor(types)
			// where types comes from arrayType.GetArrayRank()
			if (method.DeclaringType.FullName == "System.Linq.Expressions.Compiler.ILGen") {
				if (method.Name == "EmitArray" && method.Parameters.Count == 2) {
					return InterestingReason.LinqCompileReflection;
				}
			}

			// 78. linq emitting hasvalue on a nullable type, does nullableType.GetMethod("get_Value")
			if (method.DeclaringType.FullName == "System.Linq.Expressions.Compiler.ILGen") {
				if (method.Name == "EmitHasValue") {
					return InterestingReason.LinqCompileReflection;
				}
			}

			// 79. serialization moved

			// 80. xml soap reflection importer, initializing the serialization struct model
			// does FieldModel::.ctor,
			// passing in a memberInfo, fieldType. ctor does memberInfo.DeclaringType.GetMethod("houldSerialize")
			// also GetProperty(memberInfo.Name + "Specified")
			if (method.DeclaringType.FullName == "System.Xml.Serialization.FieldModel") {
				if (method.Name == ".ctor" && method.Parameters.Count == 3) {
					return InterestingReason.XmlSoapImporterFieldModelReflection;
				}
			}

			// 81. xml reflection importer, GetChoiceIdentifierType does structModel.Type.GetMember(choice.MemberName))
			if (method.DeclaringType.FullName == "System.Xml.Serialization.XmlReflectionImporter") {
				if (method.Name == "GetChoiceIdentifierType") {
					return InterestingReason.XmlReflectionImporter;
				}
			}

			// 82. xml deserialize. readobject uses ReflectionXmlSerializationReader, AddObjectsIntoTargetCollection
			// does targetCollectionType.GetMethod("Add"), and calls Invoke.
			if (method.DeclaringType.FullName == "System.Xml.Serialization.ReflectionXmlSerializationReader") {
				if (method.Name == "AddObjectsIntoTargetCollection") {
					return InterestingReason.XmlSerializationReaderCollectionAdd;
				}
			}

			// 83. serialization moved

			// 84. linq expression emit getarray element, does arrayType.GetMethod("Get")
			if (method.DeclaringType.FullName == "System.Linq.Expressions.Compiler.LambdaCompiler") {
				if (method.Name == "EmitGetArrayElement") {
					return InterestingReason.LinqCompileReflection;
				}
			}

			// 85. GetMethod("GetValueOrDefault")
			if (method.DeclaringType.FullName == "System.Linq.Expressions.Compiler.ILGen") {
				if (method.Name == "EmitGetValueOrDefault") {
					return InterestingReason.LinqCompileReflection;
				}
			}

			// 86. linq expression emit ByValParameterTypeEqual
			// emits a Call that's not understood
			if (method.DeclaringType.FullName == "System.Linq.Expressions.TypeBinaryExpression") {
				if (method.Name == "ByValParameterTypeEqual") {
					return InterestingReason.LinqExpressionCallMaybeSafe;
				}
			}

			// 87. creating lazy initialized field for linq,
			// emits a field access exppression to StrongBox<T>.Value
			// would need to ensure that Strongbox<T>.Value is kept by linker.
			if (method.DeclaringType.FullName == "System.Linq.Expressions.Compiler.LambdaCompiler") {
				if (method.Name == "CreateLazyInitializedField") {
					return InterestingReason.LinqExpressionFieldString;
				}
			}

			// 88. serialization moved
			InterestingReason r = GetSerializationReason (method);
			if (r != null) {
				return r;
			}

			// 89. xsl transform uses XmlILGenerator. iterator descriptor PushValue does GetMethod("get_Current")
			if (method.DeclaringType.FullName == "System.Xml.Xsl.IlGen.IteratorDescriptor") {
				if (method.Name == "PushValue") {
					return InterestingReason.XmlILGenGetCurrent;
				}
			}

			// 90. crypto certs create a download func. get SocketsHttpHandler type and HttpClient type by name,
			// get methods by string. createinstance, etc...
			if (method.DeclaringType.FullName == "Internal.Cryptography.Pal.CertificateAssetDownloader") {
				if (method.Name == "CreateDownloadBytesFunc") {
					return InterestingReason.CertificateDownloader;
				}
			}

			// 91. linq interpreter get array accessor does GetMethod("GetValue")
			if (method.DeclaringType.FullName == "System.Linq.Expressions.Interpreter.CallInstruction") {
				if (method.Name == "GetArrayAccessor") {
					return InterestingReason.LinqGetValue;
				}
			}

			// 92. linq lambda compiler emit uses reflection to get "Set" method
			if (method.DeclaringType.FullName == "System.Linq.Expressions.Compiler.LambdaCompiler") {
				if (method.Name == "EmitSetArrayElement") {
					return InterestingReason.LinqCompileReflection;
				}
				// emmiting binary arithmetic gets constructor of result type
				if (method.Name == "EmitLiftedBinaryArithmetic") {
					return InterestingReason.LinqCompileReflection;
				}
			}

			// 93 .linq ilgen does GetConstructor to emit an IL constant
			if (method.DeclaringType.FullName == "System.Linq.Expressions.Compiler.ILGen") {
				if (method.Name == "TryEmitILConstant" ||
					method.Name == "EmitNullableToNullableConversion" || // similar
					method.Name == "EmitNonNullableToNullableConversion" ||
					method.Name == "EmitGetValue") {
					return InterestingReason.LinqCompileReflection;
				}
			}

			// 95. XsltMethods has a helper that just does Type.GetMethod, where helper invocations all look safe.
			if (method.DeclaringType.FullName == "System.Xml.Xsl.Runtime.XsltMethods") {
				if (method.Name == "GetMethod") {
					return InterestingReason.XsltGetMethod;
				}
			}

			// 96. reflection-based xml serialization reader, used for xml deserialization
			// to write a literal struct method, does GetMethod("set_" + specifiedMemberName)
			if (method.DeclaringType.FullName == "System.Xml.Serialization.ReflectionXmlSerializationReader/<>c__DisplayClass54_1") {
				if (method.Name == "<WriteLiteralStructMethod>b__3") {
					return InterestingReason.XmlSerializationReaderGetMethod;
				}
			}

			// 97. serialization moved

			// 98. serialization moved

			// 99. callsite binder BinaryOperation does RuntimeBinder ctor. which relies on something from RuntimeBinderExtensions cctor
			// has static member func (using => syntax, so it makes a lambda)
			// which takes memberinfo, does GetMethod("HasSameMetadataDefinitionAs"), and memberInfo.GetProperty("MetadataToken")
			if (method.DeclaringType.FullName == "Microsoft.CSharp.RuntimeBinder.RuntimeBinderExtensions/<>c") {
				if (method.Name == "<.cctor>b__11_0") {
					return InterestingReason.RuntimeBinderReflection;
				}
			}

			// 100. serialization moved

			// 101. runtime binder symboltable does GetTypeByName, a wrapper for GetType.
			// only seems to be called on known types and strings, should be safe
			if (method.DeclaringType.FullName == "Microsoft.CSharp.RuntimeBinder.SymbolTable") {
				if (method.Name == "GetTypeByName") {
					return InterestingReason.RuntimeBinderReflectionSafe;
				}
			}

			// 102. serialization moved

			// 103. serialization moved

			// just the below gives 92297 methods, 30288 entry methods, 387 "interesting"
			// 1130 stacktraces
			// if (IsLinkerUnanalyzedReflectionMethod(method)) {
			//     return InterestingReason.LinkerUnanalyzed;
			// }
			return null;
		}

		private InterestingReason GetSerializationReason(MethodDefinition method)
		{
			if (method.DeclaringType.FullName.StartsWith ("System.Runtime.Serialization")) {
				// json serialization I guess? JsonFormatGeneratorStatigs::get_ExtensionDataProperty ends in GetProperty
				// should be detectable. they do things like typeof(ConcreteType).GetMethod("string")
				if (method.DeclaringType.FullName == "System.Runtime.Serialization.JsonFormatGeneratorStatics") {
					if (method.Name == "get_ExtensionDataProperty" ||
						method.Name == "get_GetCurrentMethod" ||
						method.Name == "get_TypeHandleProperty" ||
						method.Name == "get_MoveNextMethod" ||
						method.Name == "get_OnDeserializationMethod" ||
						method.Name == "get_SerializationExceptionCtor" || // this one does typeof(ConcreteType).GetConstructor()
						method.Name == "get_ExtensionDataObjectCtor") {
						return InterestingReason.JsonFormatGeneratorStatics;
					}
				}

				// XsdDataContractExporter::Export does datacontract stuff, ends up Serialization, createXmlFormatWriterDelegate
				// GenerateClassWriter, eventually end up in serialization CodeGenerator::.ctor, .cctor, which does GetProperty
				// CodeGenerator in System.Runtime.Serialization (in System.Private.DataContractSerialization)
				// does static property typeof(string).GetProperty("Length").GetMethod
				// should be afe.
				// but the static cctor does more things that look to be less safe.
				if (method.DeclaringType.FullName == "System.Runtime.Serialization.CodeGenerator") {
					if (method.Name == ".cctor") {
						return InterestingReason.CodeGeneratorStatic;
					}
					// needs investigation. some of the statics look like typeof(ConcreteType).GetMethod("string")
					// some have getters that take the type from elsewhere. I think the cctor itself is safe, and the getters are their own methods.
				}

				// similar to json, XmlFormatGeneratorStatics also have getters that would probably be detectable easily.
				if (method.DeclaringType.FullName == "System.Runtime.Serialization.XmlFormatGeneratorStatics") {
					if (method.Name == "get_ExtensionDataProperty" ||
						method.Name == "get_ExtensionDataSetExplicitMethodInfo" ||
						method.Name == "get_OnDeserializationMethod" ||
						// some of these use GetConstructor, taking binding flags that may need to be taken into account:
						// typeof(ExtensionDataObject).GetConstructor(Globals.ScanAllMembers, null, Array.Empty<Type>(), null));
						method.Name == "get_ExtensionDataObjectCtor" ||
						method.Name == "get_ExtensionDataSetExplicitMethodInfo" ||
						method.Name == "get_ExtensionDataProperty") {
						return InterestingReason.XmlFormatGeneratorStatics;
					}
				}

				// DataContractSet uses XmlFormatReaderGenerator, which does codegen.
				// trying to generate a load, (ldc(object)), it uses the static that points to Type.GetTypeFromHandle,
				// obtained using reflection. detectable. s_getTypeFromHandle = typeof(Type).GetMethod("GetTypeFromHandle");
				if (method.DeclaringType.FullName == "System.Runtime.Serialization.CodeGenerator") {
					if (method.Name == "get_GetTypeFromHandle" ||
						method.Name == "get_ObjectEquals") {
						return InterestingReason.CodeGeneratorGettersSimple;
					}
				}

				// serialization objectmanager DoValueTypeFixup tries to set value into a field of an object in a holder
				// look at parent for some field... if it's nullable, do GetField(nameof(value)). don't understand this.
				// I guess when deserializing an object graph, need to read in, build graph, then fix up pointers
				// objectholder has fixups. when fixing a pointer to boxed valuetype, DoValueTypeFixup does this.
				// GetField(nameof(value)) of the parent field's FieldType. I guess this is the field of a boxed valuetype?
				// for the nullable case. don't fully understand this.
				if (method.DeclaringType.FullName == "System.Runtime.Serialization.ObjectManager") {
					if (method.Name == "DoValueTypeFixup") {
						return InterestingReason.DeserializationValueTypeFixup;
					}
				}

				// xsd data contract schema exporter calls InvokeSchemaProviderMethod. this takes in a Type, does GetMethod(methodName)
				// methodName comes from XmlSchemaProviderAttribute on the type. would need to preserve MethodName.
				if (method.DeclaringType.FullName == "System.Runtime.Serialization.SchemaExporter") {
					if (method.Name == "InvokeSchemaProviderMethod") {
						return InterestingReason.XmlSchemaProviderAttributeMethodName;
					}
				}

				// exporting collection data contract, IsCollectionOrTryCreate
				// receives an itemType. gets its basetype, checks things, does GetConstructor, does MakeGenericType.GetMethod, etc...
				// BaseType is ok. globals TypeOf is ok.
				// GetGenericTypeDefinition is ok.
				// TypeOfKeyValue, etc... MakeGenericType is ok.
				// type.GetGenericArguments is ok.
				// does ICollection<T>... GetMethod(Globals.AddMethodName);
				// might be ok? unclear
				if (method.DeclaringType.FullName == "System.Runtime.Serialization.CollectionDataContract") {
					if (method.Name == "IsCollectionOrTryCreate") {
						return InterestingReason.CollectionDataContractRequiresAddMethodMaybeOk;
					}
				}

				// binaryformatter deserialize objectreader GetType wraps Type.GetType, calling into GetSimplyNamedTypeFromAssembly
				if (method.DeclaringType.FullName == "System.Runtime.Serialization.Formatters.Binary.ObjectReader") {
					if (method.Name == "GetSimplyNamedTypeFromAssembly") {
						return InterestingReason.BinaryFormatterGetType;
					}
				}

				// data contract serialization classdatacontract helper ctor does SetKeyValuePairAdapterFlags
				// which takes a type, and does GetMethod("GetKeyValuePair")
				if (method.DeclaringType.FullName == "System.Runtime.Serialization.ClassDataContract/ClassDataContractCriticalHelper") {
					if (method.Name == "SetKeyValuePairAdapterFlags") {
						return InterestingReason.DataContractSerializationReflection;
					}
				}

				// data contract exporter does xml serialization codegen BeginMethod
				if (method.DeclaringType.FullName == "System.Runtime.Serialization.CodeGenerator") {
					if (method.Name == "BeginMethod") {
						return InterestingReason.DataContractExporterCodeGenReflection;
					}
				}

				// exporting data contract uses XmlFormatReaderGenerator CriticalHelper, CreateObject
				if (method.DeclaringType.FullName == "System.Runtime.Serialization.XmlFormatReaderGenerator/CriticalHelper") {
					if (method.Name == "CreateObject") {
						return InterestingReason.XmlFormatReaderGeneratorSafe;
					}
				}

				// exporting datacontract, xmlformatreadergenerator helper does GetISerializableConstructor
				// does UnderlyingType.GetConstructor(Globals.ScanAllMembers
				if (method.DeclaringType.FullName == "System.Runtime.Serialization.ClassDataContract/ClassDataContractCriticalHelper") {
					if (method.Name == "GetISerializableConstructor") {
						return InterestingReason.DataContractSerializationReflection;
					}
				}

				// data contract helper uses understandable simple pattern to cache a method.
				if (method.DeclaringType.FullName == "System.Runtime.Serialization.PrimitiveDataContract") {
					if (method.Name == "get_XmlFormatReaderMethod") {
						return InterestingReason.DataContractSimple;
					}
				}

				// data contract serialization uses XmlFormatReaderGenerator, to wrap nullable object
				// does GetConstructor()). may be safe? if Nullable<T>::.ctor(T) is kept:
				// it does Globals.TypeOfNullable.MakeGenericType(innerType).GetConstructor(innerType)
				// where innertype is retrieved from parameter
				if (method.DeclaringType.FullName == "System.Runtime.Serialization.XmlFormatReaderGenerator/CriticalHelper") {
					if (method.Name == "WrapNullableObject") {
						return InterestingReason.XmlFormatReaderGeneratorMaybeSafe;
					}
				}

				if (method.DeclaringType.FullName == "System.Runtime.Serialization.SerializationException" ||
					method.DeclaringType.FullName == "System.Runtime.Serialization.DeserializationTracker" ||
					method.DeclaringType.FullName == "System.Runtime.Serialization.StreamingContext" ||
					method.DeclaringType.FullName == "System.Runtime.Serialization.SerializationInfoEnumerator" ||
					method.DeclaringType.FullName == "System.Runtime.Serialization.SerializationEntry") {
					// These types are perfectly safe
					return null;
				}

				if (method.DeclaringType.FullName == "System.Runtime.Serialization.SerializationInfo") {
					switch (method.Name) {
						case "get_AsyncDeserializationInProgress":
						case "get_DeserializationInProgress":
						case "ThrowIfDeserializationInProgress":
						case "StartDeserialization":
							// All of these are static and have no linker impact
							return null;
					}
				}

				return InterestingReason.SerializationBigHammer;
			}

			return null;
		}

		// an "interesting" method will be an endpoint in the analysis.
		// meaning that we generally want to be notified about calls to it.
		// an interesting API may still not show up in stacktraces even if called,
		// if it is dominated by a different interesting API for example.

		Dictionary<string, string> interestingReasons;
		// records a unique interestingreason for each interesting method.
		// we use this to check that we don't give multiple reasons for a method.

		void RecordReason (MethodDefinition method, InterestingReason reason)
		{
			if (interestingReasons == null) {
				interestingReasons = new Dictionary<string, string> ();
			}

			// TODO validate the specified reasons
		}

		InterestingReason GetInterestingReason (MethodDefinition method)
		{
			// there are a few cases:
			// no reflection dependencies
			//   nothing needs to be done. :)
			// detectable reflection dependencies, no attribute
			//   the linker keeps them, and there's no warning.
			//   if the detection logic understood calls to APIs that we have otherwise
			//   marked unsafe, this should treat the method as safe
			//   and not report calls to the unsafe methods (which are understood)
			//   LinkerAnalyzed
			// detectable reflection dependencies, safe attribute
			//   great - the linker keeps the dependencies, and our attribute gives
			//   some documentation - though never results in a warning.
			//   the attribtue is unnecessary in this case.
			//   (Safe takes precedence over LinkerAnalyzed)
			// detectable reflection dependencies, unsafe attribute
			//   the linker might think it's smart enough... but we actually know that a method is not safe.
			//   in this case, the detected dependencies can be kept, but it's moot
			//   because it's unsafe anyway. the warning should be reported with the
			//   unsafe attribute.
			//   (Unsafe takes precedence over LinkerAnalyzed)
			// undetectable reflection dependencies, no attributes
			//   the linker warns. (LinkerUnanalyzed)
			// undetectable reflection dependencies, with the callsite marked safe
			//   the "safe" takes precedence, and the linker should not output a warning.
			//   ("Safe" attributes take precedence over LinkerUnanalyzed)
			// undetectable reflection dependencies, with the callsite marked unsafe
			//   the "unsafe" attribute should be used for the warning, rather than giving
			//   a generic "unable to detect reflection dependency"-type warning.
			//   ("Unsafe" attributes take precedence over LinkerUnanalyzed)
			// some catch-all attributes currently are applied after LinkerUnanalyzed.
			//   this lets us focus on the relevant cases that are LinkerUnanalyzed
			//   before trying to look at everything that we have marked to investigate


			InterestingReason retReason = null;

			//
			// 1. first, check for any annotations we've explicitly added
			// to APIs. we should never be marking an API with multiple "reasons"
			//

			var annotationReason = GetInterestingReasonFromAnnotation (method);
			if (annotationReason != null && !IsBigHammer (annotationReason)) {
				RecordReason (method, annotationReason);
				if (retReason != null) {
					throw new Exception ("already gave reason for " + method);
				}
				retReason = annotationReason;
			}

			// ToInvestigate and KnownReflection are saved for later...
			// LinkerUnanalyzed takes precedence over these and a few others
			var reflectionApiReason = ReflectionApis.GetInterestingReasonForReflectionApi (method);
			if (reflectionApiReason != null && !IsBigHammer (reflectionApiReason)) {
				RecordReason (method, reflectionApiReason);
				if (retReason != null) {
					throw new Exception ("already gave reason for " + method);
				}
				retReason = reflectionApiReason;
			}

			//
			// 2. LinkerUnanalyzed for any remaining APIs that the linker
			// has tried to analyze, but wasn't able to understand
			//

			//if (IsLinkerUnanalyzedReflectionMethod (method)) {
			//	RecordReason (method, InterestingReason.LinkerUnanalyzed);
			//	if (retReason != InterestingReason.None) {
			//		// Console.WriteLine("LinkerUnanalyzed method " + method + " was given reason " + retReason);
			//	} else {
			//		retReason = InterestingReason.LinkerUnanalyzed;
			//	}
			//}

			//
			// 3. some attribute markings are lower priority than LinkerUnanalyzed.
			// so that we use big hammers to eliminate things. these are KnownReflection and ToInvestigate, and a few others
			// as a special case. this is temporary - eventually all attributes should take priority over LinkerUnanalyzed.
			//

			InterestingReason bigHammerReason = null;
			if (annotationReason != null && IsBigHammer (annotationReason)) {
				bigHammerReason = annotationReason;
			}
			if (reflectionApiReason != null && IsBigHammer (reflectionApiReason)) {
				if (bigHammerReason != null) {
					throw new Exception ("duplicate");
				}
				bigHammerReason = reflectionApiReason;
			}
			if (bigHammerReason != null) {
				RecordReason (method, bigHammerReason);
				if (retReason != null && retReason.Kind == InterestingReasonKind.LinkerUnanalyzed) {
					Console.WriteLine (method + " is LinkerUnanalyzed, also " + reflectionApiReason);
				} else if (retReason != null) {
					throw new Exception ("already gave reason for " + method + " (was " + retReason + ")");
				} else {
					retReason = bigHammerReason;
				}
			}

			return retReason;
		}
	}
}
