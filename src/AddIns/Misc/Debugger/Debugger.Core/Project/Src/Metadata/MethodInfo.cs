﻿// <file>
//     <copyright see="prj:///doc/copyright.txt"/>
//     <license see="prj:///doc/license.txt"/>
//     <owner name="David Srbecký" email="dsrbecky@gmail.com"/>
//     <version>$Revision$</version>
// </file>

using System;
using System.Collections.Generic;
using Debugger.Wrappers.CorDebug;
using Debugger.Wrappers.CorSym;
using Debugger.Wrappers.MetaData;
using ICSharpCode.NRefactory.Ast;
using Mono.Cecil.Signatures;
using System.Runtime.InteropServices;

namespace Debugger.MetaData
{
	/// <summary>
	/// Provides information about a method in a class
	/// </summary>
	public class MethodInfo: MemberInfo
	{
		MethodProps methodProps;
		
		/// <summary> Gets the name of this method </summary>
		public override string Name {
			get {
				return methodProps.Name;
			}
		}
		
		/// <summary> Gets a value indicating whether this member has the private access modifier</summary>
		public override bool IsPrivate  {
			get { return methodProps.IsPrivate; }
		}
		
		/// <summary> Gets a value indicating whether this member has the internal access modifier</summary>
		public override bool IsInternal  {
			get { return methodProps.IsInternal; }
		}
		
		/// <summary> Gets a value indicating whether this member has the protected access modifier</summary>
		public override bool IsProtected  {
			get { return methodProps.IsProtected; }
		}
		
		/// <summary> Gets a value indicating whether this member has the public access modifier</summary>
		public override bool IsPublic {
			get { return methodProps.IsPublic; }
		}
		
		/// <summary> Gets a value indicating whether the name of this method
		/// is marked as specail.</summary>
		/// <remarks> For example, property accessors are marked as special </remarks>
		public bool IsSpecialName {
			get {
				return methodProps.HasSpecialName;
			}
		}
		
		/// <summary> Gets a value indicating whether this method is static </summary>
		public override bool IsStatic {
			get {
				return methodProps.IsStatic;
			}
		}
		
		/// <summary> Gets the metadata token associated with this method </summary>
		[Debugger.Tests.Ignore]
		public override uint MetadataToken {
			get {
				return methodProps.Token;
			}
		}
		
		MethodDefSig methodDefSig;
		
		MethodDefSig MethodDefSig {
			get {
				if (methodDefSig == null) {
					SignatureReader sigReader = new SignatureReader(methodProps.SigBlob.GetData());
					methodDefSig = sigReader.GetMethodDefSig(0);
				}
				return methodDefSig;
			}
		}
		
		DebugType returnType;
		
		/// <summary> The type of the return value as specified in the method signature </summary>
		/// <returns> Null if the return type is Void</returns>
		public DebugType ReturnType {
			get {
				if (this.MethodDefSig.RetType.Void) return null;
				if (returnType == null) {
					returnType = DebugType.CreateFromSignature(this.Module, this.MethodDefSig.RetType.Type, this.DeclaringType);
				}
				return returnType;
			}
		}
		
		/// <summary>
		/// Gets the types of the parameters of the method
		/// </summary>
		public DebugType[] ParameterTypes {
			get {
				List<DebugType> types = new List<DebugType>();
				foreach(Param param in this.methodDefSig.Parameters) {
					types.Add(DebugType.CreateFromSignature(this.Module, param.Type, this.DeclaringType));
				}
				return types.ToArray();
			}
		}
		
		internal ICorDebugFunction CorFunction {
			get {
				return this.Module.CorModule.GetFunctionFromToken(this.MetadataToken);
			}
		}
		
		/// <summary> Gets value indicating whether this method should be stepped over
		/// accoring to current options </summary>
		public bool StepOver {
			get {
				Options opt = this.Process.Options;
				if (opt.StepOverNoSymbols) {
					if (this.SymMethod == null) return true;
				}
				if (opt.StepOverDebuggerAttributes) {
					if (this.HasDebuggerAttribute) return true;
				}
				if (opt.StepOverAllProperties) {
					if (this.IsProperty)return true;
				}
				if (opt.StepOverSingleLineProperties) {
					if (this.IsProperty && this.IsSingleLine) return true;
				}
				if (opt.StepOverFieldAccessProperties) {
					if (this.IsProperty && this.BackingField != null) return true;
				}
				return false;
			}
		}
		
		internal MethodInfo(DebugType declaringType, MethodProps methodProps):base (declaringType)
		{
			this.methodProps = methodProps;
		}
		
		// TODO: More accurate
		bool IsProperty {
			get {
				return this.Name.StartsWith("get_") || this.Name.StartsWith("set_");
			}
		}
		
		FieldInfo backingFieldCache;
		bool getBackingFieldCalled;
		
		/// <summary>
		/// Backing field that can be used to obtain the same value as by calling this method.
		/// </summary>
		internal FieldInfo BackingField {
			get {
				if (!getBackingFieldCalled) {
					backingFieldCache = GetBackingField();
					getBackingFieldCalled = true;
				}
				return backingFieldCache;
			}
		}
		
		// Is this method in form 'return this.field;'?
		FieldInfo GetBackingField()
		{
			if (this.IsStatic) return null; // TODO: Make work for static, 
											// the code size for static is 10/11 opposed to instance 11/12 - the ldarg.0 is missing
			if (this.ParameterCount != 0) return null;
			
			ICorDebugCode corCode;
			try {
				corCode = this.CorFunction.ILCode;
			} catch {
				return null;
			}
			
			if (corCode == null) return null;
			if (corCode.Size != 7 && corCode.Size != 12 && corCode.Size != 11) return null;
			
			byte[] code = corCode.GetCode();
			
			if (code == null) return null;
			
			/*
			string codeTxt = "";
			foreach(byte b in code) {
				codeTxt += b.ToString("X2") + " ";
			}
			process.TraceMessage("Code of " + Name + ": " + codeTxt);
			 */
			
			uint token = 0;
		
			// code generated for 'return this.field'
			if (code.Length == 12 &&
			    code[00] == 0x00 && // nop
			    code[01] == 0x02 && // ldarg.0
			    code[02] == 0x7B && // ldfld
			    code[06] == 0x04 && //   <field token>
			    code[07] == 0x0A && // stloc.0
			    code[08] == 0x2B && // br.s
			    code[09] == 0x00 && //   offset+00
			    code[10] == 0x06 && // ldloc.0
			    code[11] == 0x2A)   // ret
			{
				token = getTokenFromIL(code, 06);
			}
			
			// code generated for getter 'public int Prop { get; [set;] }'
			// (same as above, just leading nop is missing)
			if (code.Length == 11 &&
			    code[00] == 0x02 && // ldarg.0
			    code[01] == 0x7B && // ldfld
			    code[05] == 0x04 && //   <field token>
			    code[06] == 0x0A && // stloc.0
			    code[07] == 0x2B && // br.s
			    code[08] == 0x00 && //   offset+00
			    code[09] == 0x06 && // ldloc.0
			    code[10] == 0x2A)   // ret
			{
				token = getTokenFromIL(code, 05);
			}
			
			if (code.Length == 7 &&
			    code[00] == 0x02 && // ldarg.0
			    code[01] == 0x7B && // ldfld
			    code[05] == 0x04 && //   <field token>
			    code[06] == 0x2A)   // ret
			{
				token = getTokenFromIL(code, 05);
			}
			
			if (token != 0) {
				// process.TraceMessage("Token: " + token.ToString("x"));
				
				MemberInfo member = this.DeclaringType.GetMember(token);
				
				if (member == null) return null;
				if (!(member is FieldInfo)) return null;
				
				if (this.Process.Options.Verbose) {
					this.Process.TraceMessage(string.Format("Found backing field for {0}: {1}", this.FullName, member.Name));
				}
				return (FieldInfo)member;
			}
			
			return null;
		}
		
		/// <summary>
		/// Gets token from IL code.
		/// </summary>
		/// <param name="ilCode">Bytes representing the code.</param>
		/// <param name="tokenEndIndex">Index of last byte of the token.</param>
		/// <returns>IL token.</returns>
		uint getTokenFromIL(byte[] ilCode, uint tokenEndIndex)
		{
			return  ((uint)ilCode[tokenEndIndex] << 24) +
					((uint)ilCode[tokenEndIndex - 1] << 16) +
					((uint)ilCode[tokenEndIndex - 2] << 8) +
					((uint)ilCode[tokenEndIndex - 3]);
		}
		
		bool? isSingleLineCache;
		
		bool IsSingleLine {
			get {
				// Note symbols might get loaded manually later by the user
				ISymUnmanagedMethod symMethod = this.SymMethod;
				if (symMethod == null) return false; // No symbols - can not determine
				
				if (isSingleLineCache.HasValue) return isSingleLineCache.Value;
				
				List<SequencePoint> seqPoints = new List<SequencePoint>(symMethod.SequencePoints);
				seqPoints.Sort();
				
				// Remove initial "{"
				if (seqPoints.Count > 0 &&
				    seqPoints[0].Line == seqPoints[0].EndLine &&
				    seqPoints[0].EndColumn - seqPoints[0].Column <= 1) {
					seqPoints.RemoveAt(0);
				}
				
				// Remove last "}"
				int listIndex = seqPoints.Count - 1;
				if (seqPoints.Count > 0 &&
				    seqPoints[listIndex].Line == seqPoints[listIndex].EndLine &&
				    seqPoints[listIndex].EndColumn - seqPoints[listIndex].Column <= 1) {
					seqPoints.RemoveAt(listIndex);
				}
				
				// Is single line
				isSingleLineCache = seqPoints.Count == 0 || seqPoints[0].Line == seqPoints[seqPoints.Count - 1].EndLine;
				return isSingleLineCache.Value;
			}
		}
		
		bool? hasDebuggerAttributeCache;
		
		bool HasDebuggerAttribute {
			get {
				if (hasDebuggerAttributeCache.HasValue) return hasDebuggerAttributeCache.Value;
				
				hasDebuggerAttributeCache =
					// Look on the method
					HasAnyAttribute(this.Module.MetaData, methodProps.Token,
					                typeof(System.Diagnostics.DebuggerStepThroughAttribute),
					                typeof(System.Diagnostics.DebuggerNonUserCodeAttribute),
					                typeof(System.Diagnostics.DebuggerHiddenAttribute))
					||
					// Look on the type
					HasAnyAttribute(this.Module.MetaData, this.DeclaringType.Token,
					                typeof(System.Diagnostics.DebuggerStepThroughAttribute),
					                typeof(System.Diagnostics.DebuggerNonUserCodeAttribute),
					                typeof(System.Diagnostics.DebuggerHiddenAttribute));
				return hasDebuggerAttributeCache.Value;
			}
		}
		
		internal static bool HasAnyAttribute(MetaDataImport metaData, uint token, params Type[] wantedAttrTypes)
		{
			foreach(CustomAttributeProps ca in metaData.EnumCustomAttributeProps(token, 0)) {
				CorTokenType tkType = (CorTokenType)(ca.Type & 0xFF000000);
				string attributeName;
				if (tkType == CorTokenType.MemberRef) {
					MemberRefProps constructorMethod = metaData.GetMemberRefProps(ca.Type);
					attributeName = metaData.GetTypeRefProps(constructorMethod.DeclaringType).Name;
				} else if (tkType == CorTokenType.MethodDef) {
					MethodProps constructorMethod = metaData.GetMethodProps(ca.Type);
					attributeName = metaData.GetTypeDefProps(constructorMethod.ClassToken).Name;
				} else {
					throw new DebuggerException("Not expected: " + tkType);
				}
				foreach(Type wantedAttrType in wantedAttrTypes) {
					if (attributeName == wantedAttrType.FullName)
						return true;
				}
			}
			return false;
		}
		
		internal void MarkAsNonUserCode()
		{
			this.CorFunction.CastTo<ICorDebugFunction2>().SetJMCStatus(0 /* false */);
			
			if (this.Process.Options.Verbose) {
				this.Process.TraceMessage("Funciton {0} marked as non-user code", this.FullName);
			}
		}
		
		/// <summary>
		/// Get a method from a managed type, method name and argument count
		/// </summary>
		public static MethodInfo GetFromName(AppDomain appDomain, System.Type type, string methodName, int paramCount)
		{
			if (type.IsNested) throw new DebuggerException("Not implemented for nested types");
			if (type.IsGenericType) throw new DebuggerException("Not implemented for generic types");
			if (type.IsGenericParameter) throw new DebuggerException("Type can not be generic parameter");
			
			DebugType debugType = DebugType.CreateFromType(appDomain, type);
			if (debugType == null) {
				throw new DebuggerException("Type " + type.FullName + " not found");
			}
			
			foreach(MethodInfo methodInfo in debugType.GetMethods(methodName)) {
				if (methodInfo.ParameterCount == paramCount) {
					return methodInfo;
				}
			}
			throw new DebuggerException("Method " + methodName + " not found");
		}
		
		internal ISymUnmanagedMethod SymMethod {
			get {
				if (this.Module.SymReader == null) return null;
				try {
					return this.Module.SymReader.GetMethod(this.MetadataToken);
				} catch {
					return null;
				}
			}
		}
		
		/// <summary> Gets the number of paramters of this method </summary>
		public int ParameterCount {
			get {
				return this.MethodDefSig.ParamCount;
			}
		}
		
		/// <summary> Gets the name of given parameter </summary>
		/// <param name="index"> Zero-based index </param>
		public string GetParameterName(int index)
		{
			// index = 0 is return parameter
			try {
				return this.Module.MetaData.GetParamPropsForMethodIndex(this.MetadataToken, (uint)index + 1).Name;
			} catch {
				return String.Empty;
			}
		}
		
		/// <summary> Get names of all parameters in order </summary>
		[Tests.Ignore]
		public string[] ParameterNames {
			get {
				List<string> names = new List<string>();
				for(int i = 0; i < ParameterCount; i++) {
					names.Add(GetParameterName(i));
				}
				return names.ToArray();
			}
		}
		
		[Debugger.Tests.Ignore]
		public List<LocalVariableInfo> LocalVariables {
			get {
				if (this.SymMethod != null) { // TODO: Is this needed?
					return GetLocalVariables();
				} else {
					return new List<LocalVariableInfo>();
				}
			}
		}
		
		public string[] LocalVariableNames {
			get {
				List<LocalVariableInfo> vars = this.LocalVariables;
				List<string> names = new List<string>();
				for(int i = 0; i < vars.Count; i++) {
					names.Add(vars[i].Name);
				}
				names.Sort();
				return names.ToArray();
			}
		}
		
		List<LocalVariableInfo> localVariables; // Cache
		
		List<LocalVariableInfo> GetLocalVariables()
		{
			if (localVariables != null) return localVariables;
			
			localVariables = GetLocalVariablesInScope(this.SymMethod.RootScope);
			if (this.DeclaringType.IsDisplayClass || this.DeclaringType.IsYieldEnumerator) {
				// Get display class from self
				AddCapturedLocalVariables(
					localVariables,
					delegate(StackFrame context) {
						return context.GetThisValue();
					},
					this.DeclaringType
				);
				// Get dispaly classes from fields
				foreach(FieldInfo fieldInfo in this.DeclaringType.GetFields()) {
					FieldInfo fieldInfoCopy = fieldInfo;
					if (fieldInfo.Name.StartsWith("CS$")) {
						AddCapturedLocalVariables(
							localVariables,
							delegate(StackFrame context) {
								return context.GetThisValue().GetFieldValue(fieldInfoCopy);
							},
							fieldInfo.Type
						);
					}
				}
			} else {
				// Add this
				if (!this.IsStatic) {
					LocalVariableInfo thisVar = new LocalVariableInfo(
						"this",
						this.DeclaringType,
						delegate(StackFrame context) {
							return context.GetThisValue();
						}
					);
					thisVar.IsThis = true;
					localVariables.Add(thisVar);
				}
			}
			return localVariables;
		}
		
		static void AddCapturedLocalVariables(List<LocalVariableInfo> vars, ValueGetter getCaptureClass, DebugType captureClassType)
		{
			if (captureClassType.IsDisplayClass || captureClassType.IsYieldEnumerator) {
				foreach(FieldInfo fieldInfo in captureClassType.GetFields()) {
					FieldInfo fieldInfoCopy = fieldInfo;
					if (fieldInfo.Name.StartsWith("CS$")) continue; // Ignore
					LocalVariableInfo locVar = new LocalVariableInfo(
						fieldInfo.Name,
						fieldInfo.Type,
						delegate(StackFrame context) {
							return getCaptureClass(context).GetFieldValue(fieldInfoCopy);
						}
					);
					locVar.IsCaptured = true;
					if (locVar.Name.StartsWith("<>")) {
						bool hasThis = false;
						foreach(LocalVariableInfo l in vars) {
							if (l.IsThis) {
								hasThis = true;
								break;
							}
						}
						if (!hasThis && locVar.Name.EndsWith("__this")) {
							locVar.Name = "this";
							locVar.IsThis = true;
						} else {
							continue; // Ignore
						}
					}
					if (locVar.Name.StartsWith("<")) {
						int endIndex = locVar.Name.IndexOf('>');
						if (endIndex == -1) continue; // Ignore
						locVar.Name = fieldInfo.Name.Substring(1, endIndex - 1);
					}
					vars.Add(locVar);
				}
			}
		}
		
		List<LocalVariableInfo> GetLocalVariablesInScope(ISymUnmanagedScope symScope)
		{
			List<LocalVariableInfo> vars = new List<LocalVariableInfo>();
			foreach (ISymUnmanagedVariable symVar in symScope.Locals) {
				ISymUnmanagedVariable symVarCopy = symVar;
				int start;
				SignatureReader sigReader = new SignatureReader(symVar.Signature);
				LocalVarSig.LocalVariable locVarSig = sigReader.ReadLocalVariable(sigReader.Blob, 0, out start);
				DebugType locVarType = DebugType.CreateFromSignature(this.Module, locVarSig.Type, this.DeclaringType);
				// Compiler generated?
				// NB: Display class does not have the compiler-generated flag
				if ((symVar.Attributes & 1) == 1 || symVar.Name.StartsWith("CS$")) {
					// Get display class from local variable
					if (locVarType.IsDisplayClass) {
						AddCapturedLocalVariables(
							vars,
							delegate(StackFrame context) {
								return GetLocalVariableValue(context, symVarCopy);
							},
							locVarType
						);
					}
				} else {
					LocalVariableInfo locVar = new LocalVariableInfo(
						symVar.Name,
						locVarType,
						delegate(StackFrame context) {
							return GetLocalVariableValue(context, symVarCopy);
						}
					);
					vars.Add(locVar);
				}
			}
			foreach(ISymUnmanagedScope childScope in symScope.Children) {
				vars.AddRange(GetLocalVariablesInScope(childScope));
			}
			return vars;
		}
		
		static Value GetLocalVariableValue(StackFrame context, ISymUnmanagedVariable symVar)
		{
			ICorDebugValue corVal;
			try {
				corVal = context.CorILFrame.GetLocalVariable((uint)symVar.AddressField1);
			} catch (COMException e) {
				if ((uint)e.ErrorCode == 0x80131304) throw new GetValueException("Unavailable in optimized code");
				throw;
			}
			return new Value(context.AppDomain, corVal);
		}
	}
	
	public delegate Value ValueGetter(StackFrame context);
	
	public class LocalVariableInfo
	{
		ValueGetter getter;
		
		public string Name { get; internal set; }
		public DebugType Type { get; private set; }
		public bool IsThis { get; internal set; }
		public bool IsCaptured { get; internal set; }
		
		public LocalVariableInfo(string name, DebugType type, ValueGetter getter)
		{
			this.Name = name;
			this.Type = type;
			this.getter = getter;
		}
		
		public Value GetValue(StackFrame context)
		{
			return getter(context);
		}
		
		public override string ToString()
		{
			return this.Type.ToString() + " " + this.Name;
		}
	}
}
