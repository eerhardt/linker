using Mono.Cecil;
using System;
using System.Text;

namespace Mono.Linker.Analysis
{
	public static class TypeChecker
	{
		public static bool MethodsMatch (MethodReference candidate, MethodReference parent)
		{
			// name and signature should match.
			// generics... ignore for now.
			if (candidate.Name != parent.Name) {
				// Console.WriteLine("    names don't match: + " + candidate.Name + " vs " + parent.Name);
				return false;
			}

			return SignaturesMatch (candidate, parent);
		}

		// this does arbitrary type unification... what could go wrong?
		public static bool TypesMatch (TypeReference a, TypeReference b)
		{
			Console.WriteLine ("                 " + a + " has fullname " + a.FullName);
			Console.WriteLine ("                 " + b + " has fullname " + b.FullName);
			if (a.FullName == b.FullName) {
				return true;
			} else {
				return false;
			}
		}

		public static bool SignaturesMatch (MethodReference candidate, MethodReference parent)
		{
			if (!SignatureTypesMatch (candidate, parent)) {
				// Console.WriteLine("signature types don't match");
				return false;
			}

			// TODO: generic params?
			// check that signature names also match
			// 
			// for (int i = 0; i < candidate.Parameters.Count; i++) {
			//     var cParam = candidate.Parameters[i];
			//     var pParam = parent.Parameters[i];
			//     if (cParam.Name != pParam.Name) {
			//         Console.WriteLine("parameter names don't match: " + cParam.Name + " vs " + pParam.Name);
			//         return false;
			//     }
			// }

			return true;
		}

		public static bool SignatureTypesMatch (MethodReference candidate, MethodReference parent)
		{
			if (candidate.HasParameters != parent.HasParameters) {
				return false;
			}

			if (candidate.Parameters.Count != parent.Parameters.Count)
				return false;

			// TODO: return type!!!

			// TODO: generic params?
			for (int i = 0; i < candidate.Parameters.Count; i++) {
				var cParam = candidate.Parameters [i];
				var pParam = parent.Parameters [i];
				// can't use ParameterType, because that won't get generic instantiations.
				// must use extension method GetParameterType, GetReturnType.
				// if (cParam.ParameterType.MyResolve() != pParam.ParameterType.MyResolve()) {
				//     return false;
				// }
				// can't use reference equality, because we may create several versions
				// versions of the parameter.
				Console.WriteLine ("    param " + i + ": " + candidate.GetParameterType (i) + " vs " + parent.GetParameterType (i));
				if (!TypesMatch (candidate.GetParameterType (i), parent.GetParameterType (i))) {

					// for non-generics, GetParametRtype gives back the same as method.Parameters[i].ParameterType.
					// the parametertype is a typereference...
					// resolving it loses generic info.
					// but not resolving it prevents us from comparing pointers.
					// cecil gives back different typereferences.
					// => need to be able to compare arbitrary typereferences, not assuming that they will be pointer-equal.
					// could look into making cecil give same typereference back?
					// not reasonable to assume it unifies typerefs from different modules for example, let's not do that.
					Console.WriteLine ("       resolved " + cParam.ParameterType + " to " + cParam.ParameterType.Resolve ());
					// that's not right...
					if (cParam.ParameterType.Resolve () == pParam.ParameterType.Resolve ()) {
						System.Diagnostics.Debugger.Break ();
						var res = cParam.ParameterType.Resolve ();
						Console.WriteLine ("ex2: " + cParam.ParameterType.Resolve () + " == " + pParam.ParameterType.Resolve ());
						if (cParam.ParameterType is TypeSpecification) {
							// resolve() does weird things for typespecs. we know Resolve() has shown they have same element type.
							if (cParam.ParameterType.GetType () == pParam.ParameterType.GetType ()) {
								// SVEN REVISIT throw new Exception("saw mismatching param types, but typespecs have same element type and cecil type");
							}
						} else {
							// SVEN REVISIT throw new Exception("saw mismatching param types, but they are actually equal!");
						}
					}
					return false;
				} else {
					if (candidate.GetParameterType (i).Resolve () != parent.GetParameterType (i).Resolve ()) {
						// names match, but they're not the same ParameterType!
						// so far we have not encountered any such cases, because for non-generics,
						// we can just resolve the parameter type and they will be the same.
						// assuming that matching by name is enough. which it won't be.
						// SVEN REVISIT throw new Exception("haven't got here before...");
					}
					if (cParam.ParameterType.Resolve () != pParam.ParameterType.Resolve ()) {
						// "indeed, we are seeing more matches when using the generic version."
						Console.WriteLine ("ex: " + cParam.ParameterType.Resolve () + " != " + pParam.ParameterType.Resolve ());
					}
				}
			}
			return true;
		}

		static StringBuilder AppendMethodSignature (StringBuilder builder, MethodDefinition method)
		{
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

			return builder;
		}

		public static string GetSignature (MethodDefinition method) => AppendMethodSignature (new StringBuilder(), method).ToString ();

		public static string GetMethodFullNameWithSignature (MethodDefinition method)
		{
			var builder = new StringBuilder ();
			builder.Append (method.DeclaringType.FullName);
			builder.Append ("::");
			AppendMethodSignature (builder, method);
			return builder.ToString ();
		}
	}
}