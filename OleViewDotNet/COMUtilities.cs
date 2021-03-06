﻿//    This file is part of OleViewDotNet.
//    Copyright (C) James Forshaw 2014
//
//    OleViewDotNet is free software: you can redistribute it and/or modify
//    it under the terms of the GNU General Public License as published by
//    the Free Software Foundation, either version 3 of the License, or
//    (at your option) any later version.
//
//    OleViewDotNet is distributed in the hope that it will be useful,
//    but WITHOUT ANY WARRANTY; without even the implied warranty of
//    MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
//    GNU General Public License for more details.
//
//    You should have received a copy of the GNU General Public License
//    along with OleViewDotNet.  If not, see <http://www.gnu.org/licenses/>.

using Microsoft.CSharp;
using Microsoft.Win32;
using Microsoft.Win32.SafeHandles;
using System;
using System.CodeDom;
using System.CodeDom.Compiler;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace OleViewDotNet
{
    public enum ObjectSafetyFlags
    {
        INTERFACESAFE_FOR_UNTRUSTED_CALLER	= 0x00000001,
        INTERFACESAFE_FOR_UNTRUSTED_DATA = 	0x00000002,
        INTERFACE_USES_DISPEX = 0x00000004,
        INTERFACE_USES_SECURITY_MANAGER = 0x00000008
    }

    [ComImport, Guid("CB5BDC81-93C1-11CF-8F20-00805F2CD064"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IObjectSafety
    {
        void GetInterfaceSafetyOptions(ref Guid riid, out uint pdwSupportedOptions, out uint pdwEnabledOptions);
        void SetInterfaceSafetyOptions(ref Guid riid, uint dwOptionSetMask, uint dwEnabledOptions);
    }

    class TypeLibCallback : ITypeLibImporterNotifySink
    {
        public Assembly ResolveRef(object tl)
        {
            return COMUtilities.ConvertTypeLibToAssembly((ITypeLib)tl, _progress);            
        }

        public void ReportEvent(ImporterEventKind eventKind, int eventCode, string eventMsg)
        {
            if ((eventKind == ImporterEventKind.NOTIF_TYPECONVERTED) && (_progress != null))
            {
                _progress.Report(new Tuple<string, int>(eventMsg, -1));
            }
        }

        public TypeLibCallback(IProgress<Tuple<string, int>> progress)
        {
            _progress = progress;
        }

        private IProgress<Tuple<string, int>> _progress;
    }

    [Flags]
    public enum CLSCTX
    {
        INPROC_SERVER = 0x1,
        INPROC_HANDLER = 0x2,
        LOCAL_SERVER = 0x4,
        INPROC_SERVER16 = 0x8,
        REMOTE_SERVER = 0x10,
        INPROC_HANDLER16 = 0x20,
        RESERVED1 = 0x40,
        RESERVED2 = 0x80,
        RESERVED3 = 0x100,
        RESERVED4 = 0x200,
        NO_CODE_DOWNLOAD = 0x400,
        RESERVED5 = 0x800,
        NO_CUSTOM_MARSHAL = 0x1000,
        ENABLE_CODE_DOWNLOAD = 0x2000,
        NO_FAILURE_LOG = 0x4000,
        DISABLE_AAA = 0x8000,
        ENABLE_AAA = 0x10000,
        FROM_DEFAULT_CONTEXT = 0x20000,
        ACTIVATE_32_BIT_SERVER = 0x40000,
        ACTIVATE_64_BIT_SERVER = 0x80000,
        APPCONTAINER = 0x400000,
        ACTIVATE_AAA_AS_IU = 0x800000,
        PS_DLL = unchecked((int)0x80000000),
        SERVER = INPROC_SERVER | LOCAL_SERVER | REMOTE_SERVER,
        ALL = INPROC_SERVER | INPROC_HANDLER | LOCAL_SERVER | REMOTE_SERVER
    }

    [Flags]
    public enum STGM
    {
        READ = 0x00000000,
        WRITE = 0x00000001,
        READWRITE = 0x00000002,
        SHARE_DENY_NONE = 0x00000040,
        SHARE_DENY_READ = 0x00000030,
        SHARE_DENY_WRITE = 0x00000020,
        SHARE_EXCLUSIVE = 0x00000010,
        PRIORITY = 0x00040000,
        CREATE = 0x00001000,
        CONVERT = 0x00020000,
        FAILIFTHERE = READ,
        DIRECT = READ,
        TRANSACTED = 0x00010000,
        NOSCRATCH = 0x00100000,
        NOSNAPSHOT = 0x00200000,
        SIMPLE = 0x08000000,
        DIRECT_SWMR = 0x00400000,
        DELETEONRELEASE = 0x04000000
    }

    [Flags]
    public enum EOLE_AUTHENTICATION_CAPABILITIES
    {
        NONE = 0,
        MUTUAL_AUTH = 0x1,
        STATIC_CLOAKING = 0x20,
        DYNAMIC_CLOAKING = 0x40,
        ANY_AUTHORITY = 0x80,
        MAKE_FULLSIC = 0x100,
        DEFAULT = 0x800,
        SECURE_REFS = 0x2,
        ACCESS_CONTROL = 0x4,
        APPID = 0x8,
        DYNAMIC = 0x10,
        REQUIRE_FULLSIC = 0x200,
        AUTO_IMPERSONATE = 0x400,
        NO_CUSTOM_MARSHAL = 0x2000,
        DISABLE_AAA = 0x1000
    }

    public enum RPC_AUTHN_LEVEL
    {
        DEFAULT       = 0,
        NONE          = 1,
        CONNECT       = 2,
        CALL          = 3,
        PKT           = 4,
        PKT_INTEGRITY = 5,
        PKT_PRIVACY   = 6,
    }

    public enum RPC_IMP_LEVEL
    {
        DEFAULT      = 0,
        ANONYMOUS    = 1,
        IDENTIFY     = 2,
        IMPERSONATE  = 3,
        DELEGATE     = 4,
    }

    public enum MSHCTX
    {
        LOCAL = 0,
        NOSHAREDMEM = 1,
        DIFFERENTMACHINE = 2,
        INPROC = 3,
        CROSSCTX = 4
    }

    public enum MSHLFLAGS
    {
        NORMAL = 0,
        TABLESTRONG = 1,
        TABLEWEAK = 2,
        NOPING = 4
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct OptionalGuid : IDisposable
    {
        IntPtr pGuid;

        void IDisposable.Dispose()
        {
            if (pGuid != IntPtr.Zero)
            {
                Marshal.FreeCoTaskMem(pGuid);
                pGuid = IntPtr.Zero;
            }
        }

        public OptionalGuid(Guid guid)
        {
            pGuid = Marshal.AllocCoTaskMem(16);
            Marshal.Copy(guid.ToByteArray(), 0, pGuid, 16);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct MULTI_QI : IDisposable
    {
        OptionalGuid pIID;
        IntPtr pItf;
        int hr;

        public object GetObject()
        {
            if (pItf == IntPtr.Zero)
            {
                return null;
            }
            else
            {
                return Marshal.GetObjectForIUnknown(pItf);
            }
        }

        public IntPtr GetObjectPointer()
        {
            if (pItf != IntPtr.Zero)
            {
                Marshal.AddRef(pItf);
            }
            return pItf;
        }

        public int HResult()
        {
            return hr;
        }

        void IDisposable.Dispose()
        {
            ((IDisposable)pIID).Dispose();
            if (pItf != IntPtr.Zero)
            {
                Marshal.Release(pItf);
                pItf = IntPtr.Zero;
            }
        }

        public MULTI_QI(Guid iid)
        {
            pIID = new OptionalGuid(iid);
            pItf = IntPtr.Zero;
            hr = 0;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    public sealed class COSERVERINFO : IDisposable
    {
        int dwReserved1;
        [MarshalAs(UnmanagedType.LPWStr)]
        string pwszName;
        IntPtr pAuthInfo;
        int dwReserved2;

        void IDisposable.Dispose()
        {
            if (pAuthInfo != IntPtr.Zero)
            {
                Marshal.FreeCoTaskMem(pAuthInfo);
            }
        }

        public COSERVERINFO(string name)
        {
            pwszName = name;
        }   
    }

    [StructLayout(LayoutKind.Sequential)]
    public class BIND_OPTS3
    {
        int cbStruct;
        public int grfFlags;
        public int grfMode;
        public int dwTickCountDeadline;
        public int dwTrackFlags;
        public CLSCTX dwClassContext;
        public int locale;
        public IntPtr pServerInfo;
        public IntPtr hwnd;

        public BIND_OPTS3()
        {
            cbStruct = Marshal.SizeOf(this);
        }
    }

    [Flags]
    public enum CreateUrlMonikerFlags
    {
        Legacy = 0,
        Uniform = 1,
        NoCanonicalize = 2,
    }

    public static class COMUtilities
    {
        private enum RegKind
        {
            RegKind_Default = 0,
            RegKind_Register = 1,
            RegKind_None = 2
        }

        [DllImport("oleaut32.dll", CharSet = CharSet.Unicode, PreserveSig = false)]
        private static extern void LoadTypeLibEx(String strTypeLibName, RegKind regKind,
            [MarshalAs(UnmanagedType.Interface)] out ITypeLib typeLib);
        [DllImport("ole32.dll", EntryPoint = "CoCreateInstance", CallingConvention = CallingConvention.StdCall)]
        public static extern int CoCreateInstance(ref Guid rclsid, IntPtr pUnkOuter, CLSCTX dwClsContext, ref Guid riid, out IntPtr ppv);
        [DllImport("ole32.dll", EntryPoint = "CoCreateInstanceEx", CallingConvention = CallingConvention.StdCall)]
        public static extern int CoCreateInstanceEx(ref Guid rclsid, IntPtr punkOuter, CLSCTX dwClsCtx, [In] COSERVERINFO pServerInfo, int dwCount, [In, Out] MULTI_QI[] pResults);
        [DllImport("ole32.dll", EntryPoint = "CoGetClassObject", CallingConvention = CallingConvention.StdCall)]
        public static extern int CoGetClassObject(ref Guid rclsid, CLSCTX dwClsContext, [In] COSERVERINFO pServerInfo, ref Guid riid, out IntPtr ppv);
        //[DllImport("ole32.dll", EntryPoint = "CoUnmarshalInterface", CallingConvention = CallingConvention.StdCall, PreserveSig = false)]
        //public static extern void CoUnmarshalInterface(IStream stm, ref Guid riid, [MarshalAs(UnmanagedType.IUnknown)] out object ppv);

        [DllImport("ole32.dll", CharSet = CharSet.Unicode, CallingConvention = CallingConvention.StdCall, PreserveSig = false)]
        public static extern void CoGetObject(string pszName, BIND_OPTS3 pBindOptions, ref Guid riid, [MarshalAs(UnmanagedType.IUnknown)] out object ppv);

        [return: MarshalAs(UnmanagedType.Interface)]
        [DllImport("ole32.dll", ExactSpelling = true, PreserveSig = false)]
        public static extern IBindCtx CreateBindCtx([In] uint reserved);

        [DllImport("ole32.dll", ExactSpelling = true, PreserveSig = false)]
        public static extern IMoniker CreateObjrefMoniker([MarshalAs(UnmanagedType.Interface)] object punk);

        [DllImport("shlwapi.dll", CharSet = CharSet.Unicode, CallingConvention = CallingConvention.StdCall, PreserveSig = false)]
        public extern static void SHCreateStreamOnFile(string pszFile, STGM grfMode, out IntPtr ppStm);

        [DllImport("ole32.dll", CallingConvention = CallingConvention.StdCall, CharSet = CharSet.Unicode)]
        public static extern int CoGetInstanceFromFile(
            IntPtr pServerInfo,
            OptionalGuid pClsid,
            IntPtr punkOuter,
            CLSCTX dwClsCtx,
            STGM grfMode,
            string pwszName,
            int dwCount,
            [In, Out] MULTI_QI[] pResults
        );

        [DllImport("ole32.dll", CallingConvention = CallingConvention.StdCall, CharSet = CharSet.Unicode, PreserveSig = false)]
        public static extern void GetClassFile(string szFilename, out Guid clsid);

        [DllImport("ole32.dll", CallingConvention = CallingConvention.StdCall, CharSet = CharSet.Unicode, PreserveSig = false)]
        public static extern IMoniker MkParseDisplayName(IBindCtx pbc, string szUserName, out int pchEaten);

        [DllImport("urlmon.dll", CallingConvention = CallingConvention.StdCall, CharSet = CharSet.Unicode)]
        public static extern int CreateURLMonikerEx(IMoniker pMkCtx,
                                                    string szURL,
                                                    out IMoniker ppmk,
                                                    CreateUrlMonikerFlags dwFlags);

        [DllImport("ole32.dll", CallingConvention = CallingConvention.StdCall, CharSet = CharSet.Unicode)]
        public static extern int CLSIDFromProgID(string lpszProgID, out Guid lpclsid);

        [DllImport("ole32.dll", CallingConvention = CallingConvention.StdCall, CharSet = CharSet.Unicode, PreserveSig = false)]
        public static extern void CoMarshalInterface(IStream pStm, ref Guid riid,
            [MarshalAs(UnmanagedType.Interface)] object pUnk, MSHCTX dwDestContext, IntPtr pvDestContext, MSHLFLAGS mshlflags);

        [DllImport("ole32.dll", CallingConvention = CallingConvention.StdCall, CharSet = CharSet.Unicode, PreserveSig = false)]
        public static extern void CoReleaseMarshalData(
              IStream pStm
            );

        [DllImport("ole32.dll", CallingConvention = CallingConvention.StdCall, CharSet = CharSet.Unicode, PreserveSig = false)]
        [return: MarshalAs(UnmanagedType.Interface)]
        public static extern object CoUnmarshalInterface(
            IStream pStm,
            ref Guid riid
        );

        private static Dictionary<Guid, Assembly> m_typelibs;
        private static Dictionary<string, Assembly> m_typelibsname;
        private static Dictionary<Guid, Type> m_iidtypes;

        static COMUtilities()
        {
            AppDomain.CurrentDomain.AssemblyResolve += CurrentDomain_AssemblyResolve;
        }

        static Assembly CurrentDomain_AssemblyResolve(object sender, ResolveEventArgs args)
        {
            if (m_typelibsname != null)
            {
                lock (m_typelibsname)
                {
                    if (m_typelibsname.ContainsKey(args.Name))
                    {
                        return m_typelibsname[args.Name];
                    }
                }
            }

            return null;
        }

        public static string ReadStringFromKey(RegistryKey rootKey, string keyName, string valueName)
        {
            RegistryKey key = rootKey;

            try
            {
                if (keyName != null)
                {
                    key = rootKey.OpenSubKey(keyName);
                }

                string valueString = String.Empty;
                if (key != null)
                {
                    object valueObject = key.GetValue(valueName);
                    if (valueObject != null)
                    {
                        valueString = valueObject.ToString();
                    }
                }

                return valueString.TrimEnd('\0');
            }
            finally
            {
                if (key != null && key != rootKey)
                {
                    key.Close();
                }
            }
        }

        public static int ReadIntFromKey(RegistryKey rootKey, string keyName, string valueName)
        {
            string value = ReadStringFromKey(rootKey, keyName, valueName);
            if (value != null)
            {
                int ret;
                if (int.TryParse(value, out ret))
                {
                    return ret;
                }
            }
            return 0;
        }

        public static Guid ReadGuidFromKey(RegistryKey rootKey, string keyName, string valueName)
        {
            string guid = ReadStringFromKey(rootKey, keyName, valueName);
            if (guid != null && IsValidGUID(guid))
            {
                return new Guid(guid);
            }
            return Guid.Empty;
        }

        public static string GetCategoryName(Guid catid)
        {
            Guid clsid = new Guid("{0002E005-0000-0000-C000-000000000046}");
            Guid iid = typeof(ICatInformation).GUID;
            IntPtr pCatMgr;
            string strDesc = String.Empty;

            if (CoCreateInstance(ref clsid, IntPtr.Zero, CLSCTX.INPROC_SERVER, ref iid, out pCatMgr) == 0)
            {
                ICatInformation catInfo = (ICatInformation)Marshal.GetObjectForIUnknown(pCatMgr);
                IntPtr pStrDesc;

                try
                {
                    catInfo.GetCategoryDesc(ref catid, 0, out pStrDesc);
                    strDesc = Marshal.PtrToStringUni(pStrDesc);
                    Marshal.FreeCoTaskMem(pStrDesc);
                }
                catch (COMException)
                {
                }

                Marshal.ReleaseComObject(catInfo);
                Marshal.Release(pCatMgr);
            }

            if (String.IsNullOrWhiteSpace(strDesc))
            {
                if (catid == new Guid("59fb2056-d625-48d0-a944-1a85b5ab2640"))
                {
                    strDesc = "AppContainer Compatible";
                }
                else
                {
                    strDesc = catid.FormatGuid();
                }
            }

            return strDesc;
        }

        public static string GetAppDirectory()
        {
            return Path.GetDirectoryName(new Uri(Assembly.GetCallingAssembly().CodeBase).LocalPath);
        }

        public static string Get32bitExePath()
        {
            return Path.Combine(GetAppDirectory(), "OleViewDotNet32.exe");
        }

        public static string GetExePath()
        {
            return Path.Combine(GetAppDirectory(), "OleViewDotNet.exe");
        }

        public static string GetAppDataDirectory()
        {
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "OleViewDotNet");
        }

        public static string GetTypeLibDirectory()
        {
            return Path.Combine(GetAppDataDirectory(), "typelib");
        }

        public static string GetPluginDirectory()
        {
            return Path.Combine(GetAppDirectory(), "plugin");
        }

        private static Regex m_guidRegex = null;

        public static bool IsValidGUID(string guid)
        {
            if (m_guidRegex == null)
            {
                m_guidRegex = new Regex("[A-Fa-f0-9]{8}-[A-Fa-f0-9]{4}-[A-Fa-f0-9]{4}-[A-Fa-f0-9]{4}-[A-Fa-f0-9]{12}");
            }

            return m_guidRegex.IsMatch(guid);
        }

        private static void RegisterTypeInterfaces(Assembly a)
        {
            Type[] types = a.GetTypes();

            foreach (Type t in types)
            {
                if (t.IsInterface)
                {
                    InterfaceViewers.InterfaceViewers.AddFactory(new InterfaceViewers.InstanceTypeViewerFactory(t));
                    if (!m_iidtypes.ContainsKey(t.GUID))
                    {
                        m_iidtypes.Add(t.GUID, t);
                    }
                }
            }
        }

        private static void LoadBuiltinTypes(Assembly asm)
        {
            foreach (Type t in asm.GetTypes().Where(x => x.IsPublic && x.IsInterface && IsComImport(x)))
            {
                if (!m_iidtypes.ContainsKey(t.GUID))
                {
                    m_iidtypes.Add(t.GUID, t);
                }
            }
        }

        public static Type GetInterfaceType(Guid iid)
        {
            if (m_iidtypes == null)
            {
                LoadTypeLibAssemblies();
            }

            if (m_iidtypes.ContainsKey(iid))
            {
                return m_iidtypes[iid];
            }
            return null;
        }

        public static void LoadTypeLibAssemblies()
        {
            if (m_typelibs == null)
            {
                try
                {
                    string strTypeLibDir = GetTypeLibDirectory();
                    Directory.CreateDirectory(strTypeLibDir);
                    string[] files = Directory.GetFiles(strTypeLibDir, "*.dll");

                    m_typelibs = new Dictionary<Guid, Assembly>();
                    m_iidtypes = new Dictionary<Guid, Type>();
                    m_typelibsname = new Dictionary<string, Assembly>();

                    foreach (string f in files)
                    {
                        try
                        {
                            Assembly a = Assembly.LoadFrom(f);
                            if (!m_typelibs.ContainsKey(Marshal.GetTypeLibGuidForAssembly(a)))
                            {
                                m_typelibs.Add(Marshal.GetTypeLibGuidForAssembly(a), a);

                                lock (m_typelibsname)
                                {
                                    m_typelibsname[a.FullName] = a;
                                }

                                RegisterTypeInterfaces(a);
                            }
                        }
                        catch (Exception e)
                        {
                            System.Diagnostics.Debug.WriteLine(e.ToString());
                        }
                    }
                }
                catch (Exception e)
                {
                    System.Diagnostics.Debug.WriteLine(e.ToString());
                }

                LoadBuiltinTypes(Assembly.GetExecutingAssembly());
                LoadBuiltinTypes(typeof(int).Assembly);
            }
        }

        public static Assembly LoadTypeLib(string path, IProgress<Tuple<string, int>> progress)
        {
            ITypeLib typeLib = null;

            try
            {
                LoadTypeLibEx(path, RegKind.RegKind_Default, out typeLib);

                return ConvertTypeLibToAssembly(typeLib, progress);
            }
            finally
            {
                if (typeLib != null)
                {
                    Marshal.ReleaseComObject(typeLib);
                }
            }
        }

        public static Assembly LoadTypeLib(ITypeLib typeLib, IProgress<Tuple<string, int>> progress)
        {
            try
            {
                return ConvertTypeLibToAssembly(typeLib, progress);
            }
            finally
            {
                if (typeLib != null)
                {
                    Marshal.ReleaseComObject(typeLib);
                }
            }
        }

        public static Assembly ConvertTypeLibToAssembly(ITypeLib typeLib, IProgress<Tuple<string, int>> progress)
        {
            if (m_typelibs == null)
            {
                if (progress != null)
                {
                    progress.Report(new Tuple<string, int>("Initializing Global Libraries", -1));
                }
                LoadTypeLibAssemblies();
            }

            if (m_typelibs.ContainsKey(Marshal.GetTypeLibGuid(typeLib)))
            {
                return m_typelibs[Marshal.GetTypeLibGuid(typeLib)];
            }
            else
            {
                string strAssemblyPath = GetTypeLibDirectory();
                strAssemblyPath = Path.Combine(strAssemblyPath, Marshal.GetTypeLibGuid(typeLib).ToString() + ".dll");

                TypeLibConverter conv = new TypeLibConverter();
                AssemblyBuilder asm = conv.ConvertTypeLibToAssembly(typeLib, strAssemblyPath, TypeLibImporterFlags.ReflectionOnlyLoading,
                                        new TypeLibCallback(progress), null, null, null, null);
                asm.Save(Path.GetFileName(strAssemblyPath));
                Assembly a = Assembly.LoadFile(strAssemblyPath);

                m_typelibs[Marshal.GetTypeLibGuid(typeLib)] = a;
                lock (m_typelibsname)
                {
                    m_typelibsname[a.FullName] = a;
                }
                RegisterTypeInterfaces(a);

                return a;
            }
        }

        public static Type GetDispatchTypeInfo(IWin32Window parent, object comObj)
        {
            Type ret = null;

            try
            {
                if (!comObj.GetType().IsCOMObject)
                {
                    ret = comObj.GetType();
                }
                else
                {
                    IntPtr typeInfo = IntPtr.Zero;

                    try
                    {
                        IDispatch disp = (IDispatch)comObj;

                        disp.GetTypeInfo(0, 0x409, out typeInfo);

                        ITypeInfo ti = (ITypeInfo)Marshal.GetObjectForIUnknown(typeInfo);
                        ITypeLib tl = null;
                        int iIndex = 0;
                        ti.GetContainingTypeLib(out tl, out iIndex);
                        Guid typelibGuid = Marshal.GetTypeLibGuid(tl);
                        Assembly asm = LoadTypeLib(parent, tl);

                        if (asm != null)
                        {
                            string name = Marshal.GetTypeInfoName(ti);
                            ret = asm.GetTypes().First(t => t.Name == name);
                        }
                    }
                    catch (Exception)
                    {
                    }
                    finally
                    {
                        if (typeInfo != IntPtr.Zero)
                        {
                            Marshal.Release(typeInfo);
                        }
                    }
                }
            }
            catch (Exception)
            {
            }

            return ret;
        }

        public static RawSecurityDescriptor ReadSecurityDescriptorFromKey(RegistryKey key, string value)
        {
            byte[] data = (byte[])key.GetValue(value);

            if (data != null)
            {
                return new RawSecurityDescriptor(data, 0);
            }
            else
            {
                return null;
            }
        }

        public static bool IsComImport(Type t)
        {
            return t.GetCustomAttributes(typeof(ComImportAttribute), false).Length > 0;
        }

        private static Dictionary<Type, Type> _wrappers = new Dictionary<Type, Type>();

        private static CodeParameterDeclarationExpression GetParameter(ParameterInfo pi)
        {
            Type baseType = pi.ParameterType;

            if (baseType.IsByRef)
            {
                string name = baseType.FullName.TrimEnd('&');

                baseType = baseType.Assembly.GetType(name);
            }

            CodeParameterDeclarationExpression p = new CodeParameterDeclarationExpression(baseType, pi.Name);
            FieldDirection d = FieldDirection.In;

            if ((pi.Attributes & ParameterAttributes.Out) == ParameterAttributes.Out)
            {
                d = FieldDirection.Out;
            }

            if ((pi.Attributes & ParameterAttributes.In) == ParameterAttributes.In)
            {
                if (d == FieldDirection.Out)
                {
                    d = FieldDirection.Ref;
                }
                else
                {
                    d = FieldDirection.In;
                }
            }

            p.Direction = d;

            return p;
        }

        private static CodeMemberMethod CreateForwardingMethod(MethodInfo mi)
        {
            CodeMemberMethod method = new CodeMemberMethod();
            method.Attributes = MemberAttributes.Public | MemberAttributes.Final;
            method.Name = mi.Name;
            method.ReturnType = new CodeTypeReference(mi.ReturnType);

            List<CodeExpression> parameters = new List<CodeExpression>();

            foreach (ParameterInfo pi in mi.GetParameters())
            {
                CodeParameterDeclarationExpression p = GetParameter(pi);
                method.Parameters.Add(p);
                parameters.Add(new CodeDirectionExpression(p.Direction, new CodeVariableReferenceExpression(pi.Name)));
            }

            CodeMethodInvokeExpression invokeExpr = new CodeMethodInvokeExpression(
                new CodeFieldReferenceExpression(new CodeThisReferenceExpression(), "_target"),
                mi.Name, parameters.ToArray());

            if (mi.ReturnType != typeof(void))
            {
                method.Statements.Add(new CodeMethodReturnStatement(invokeExpr));
            }
            else
            {
                method.Statements.Add(invokeExpr);
            }

            return method;
        }

        private static CodeMemberProperty CreateForwardingProperty(PropertyInfo pi)
        {
            CodeMemberProperty prop = new CodeMemberProperty();
            prop.Attributes = MemberAttributes.Public | MemberAttributes.Final;
            prop.Name = pi.Name;
            prop.Type = new CodeTypeReference(pi.PropertyType);

            CodePropertyReferenceExpression propExpr = new CodePropertyReferenceExpression(
                new CodeFieldReferenceExpression(new CodeThisReferenceExpression(), "_target"),
                pi.Name);

            if (pi.CanRead)
            {
                prop.GetStatements.Add(new CodeMethodReturnStatement(propExpr));
            }

            if (pi.CanWrite)
            {
                prop.SetStatements.Add(new CodeAssignStatement(propExpr, new CodeVariableReferenceExpression("value")));
            }

            return prop;
        }

        private static CodeTypeDeclaration CreateWrapperTypeDeclaration(Type t)
        {
            CodeTypeDeclaration type = new CodeTypeDeclaration(t.Name + "Wrapper");
            CodeTypeReference typeRef = new CodeTypeReference(t);

            type.IsClass = true;
            type.Attributes = MemberAttributes.Public | MemberAttributes.Final;
            type.BaseTypes.Add(typeRef);

            type.Members.Add(new CodeMemberField(typeRef, "_target"));

            CodeConstructor defaultConstructor = new CodeConstructor();
            defaultConstructor.Attributes = MemberAttributes.Public;
            defaultConstructor.Parameters.Add(new CodeParameterDeclarationExpression(new CodeTypeReference(typeof(object)), "target"));
            defaultConstructor.Statements.Add(new CodeAssignStatement(new CodeFieldReferenceExpression(new CodeThisReferenceExpression(), "_target"), new CodeCastExpression(typeRef, new CodeVariableReferenceExpression("target"))));
            type.Members.Add(defaultConstructor);

            foreach (MethodInfo mi in t.GetMethods())
            {
                if (!mi.IsSpecialName)
                {
                    type.Members.Add(CreateForwardingMethod(mi));
                }
            }

            foreach (PropertyInfo pi in t.GetProperties())
            {
                type.Members.Add(CreateForwardingProperty(pi));
            }

            return type;
        }


        private static Type CreateWrapper(Type t)
        {
            Type ret = null;
            CodeCompileUnit unit = new CodeCompileUnit();
            CodeNamespace ns = new CodeNamespace();

            CSharpCodeProvider provider = new CSharpCodeProvider();

            CodeTypeDeclaration type = CreateWrapperTypeDeclaration(t);

            ns.Types.Add(type);
            unit.Namespaces.Add(ns);

            StringBuilder builder = new StringBuilder();
            CodeGeneratorOptions options = new CodeGeneratorOptions();
            options.IndentString = "    ";
            options.BlankLinesBetweenMembers = false;

            TextWriter writer = new StringWriter(builder);

            provider.GenerateCodeFromCompileUnit(unit, writer, options);


            writer.Close();

            File.WriteAllText("dump.cs", builder.ToString());

            try
            {
                CompilerParameters compileParams = new CompilerParameters();
                TempFileCollection tempFiles = new TempFileCollection(Path.GetTempPath(), false);

                compileParams.GenerateExecutable = false;
                compileParams.GenerateInMemory = true;
                compileParams.IncludeDebugInformation = true;
                compileParams.TempFiles = tempFiles;
                compileParams.ReferencedAssemblies.Add("System.dll");
                compileParams.ReferencedAssemblies.Add("Microsoft.CSharp.dll");
                compileParams.ReferencedAssemblies.Add("System.Core.dll");
                compileParams.ReferencedAssemblies.Add(t.Assembly.Location);

                CompilerResults results = provider.CompileAssemblyFromDom(compileParams, unit);

                if (results.Errors.HasErrors)
                {
                    foreach (CompilerError e in results.Errors)
                    {
                        System.Diagnostics.Debug.WriteLine(e.ToString());
                    }
                }
                else
                {
                    ret = results.CompiledAssembly.GetType(t.Name + "Wrapper");
                    if (ret != null)
                    {
                        lock (_wrappers)
                        {
                            _wrappers[t] = ret;
                        }
                    }
                }

            }
            catch (Exception)
            {
            }

            return ret;
        }

        public static dynamic CreateDynamicCallWrapper(object target, Type t)
        {
            Type instanceType = null;

            lock (_wrappers)
            {
                if (_wrappers.ContainsKey(t))
                {
                    instanceType = _wrappers[t];
                }
            }

            if (instanceType == null)
            {
                instanceType = CreateWrapper(t);
            }

            if (instanceType != null)
            {
                return Activator.CreateInstance(instanceType, target);
            }
            else
            {
                return null;
            }
        }

        public static void SaveObjectToStream(object obj, Stream stm)
        {
            IStreamImpl istm = new IStreamImpl(stm);

            IPersistStream ps = obj as IPersistStream;

            if (ps != null)
            {
                ps.Save(istm, false);
            }
            else
            {
                IPersistStreamInit psi = (IPersistStreamInit)obj;

                psi.Save(istm, false);
            }
        }

        public static void LoadObjectFromStream(object obj, Stream stm)
        {
            IStreamImpl istm = new IStreamImpl(stm);

            IPersistStream ps = obj as IPersistStream;

            if (ps != null)
            {
                ps.Load(istm);
            }
            else
            {
                IPersistStreamInit psi = (IPersistStreamInit)obj;

                psi.InitNew();
                psi.Load(istm);
            }
        }

        public static void OleSaveToStream(object obj, Stream stm)
        {
            using (BinaryWriter writer = new BinaryWriter(stm))
            {
                Guid clsid = GetObjectClass(obj);

                writer.Write(clsid.ToByteArray());

                SaveObjectToStream(obj, stm);
            }
        }

        public static object OleLoadFromStream(Stream stm, out Guid clsid)
        {
            using (BinaryReader reader = new BinaryReader(stm))
            {
                clsid = new Guid(reader.ReadBytes(16));

                Guid unk = COMInterfaceEntry.IID_IUnknown;
                IntPtr pObj;
                object ret;

                int iError = COMUtilities.CoCreateInstance(ref clsid, IntPtr.Zero, CLSCTX.SERVER,
                    ref unk, out pObj);

                if (iError != 0)
                {
                    Marshal.ThrowExceptionForHR(iError);
                }

                ret = Marshal.GetObjectForIUnknown(pObj);
                Marshal.Release(pObj);

                LoadObjectFromStream(ret, stm);

                return ret;
            }
        }

        public static object CreateFromMoniker(string moniker, BIND_OPTS3 bind_opts)
        {
            object ret;
            Guid iid = COMInterfaceEntry.IID_IUnknown;
            CoGetObject(moniker, bind_opts, ref iid, out ret);
            return ret;
        }

        public static object CreateFromMoniker(string moniker, CLSCTX clsctx)
        {
            BIND_OPTS3 bind_opts = new BIND_OPTS3();
            bind_opts.dwClassContext = clsctx;
            return CreateFromMoniker(moniker, bind_opts);
        }

        public static object UnmarshalObject(Stream stm, Guid iid)
        {
            return CoUnmarshalInterface(new IStreamImpl(stm), ref iid);
        }

        public static object UnmarshalObject(byte[] objref)
        {
            return UnmarshalObject(new MemoryStream(objref), COMInterfaceEntry.IID_IUnknown);
        }

        public static Guid GetObjectClass(object p)
        {
            Guid ret = Guid.Empty;

            try
            {
                if (p is IPersist)
                {
                    ((IPersist)p).GetClassID(out ret);
                }
                else if (p is IPersistStream)
                {
                    ((IPersistStream)p).GetClassID(out ret);
                }
                else if (p is IPersistStreamInit)
                {
                    ((IPersistStreamInit)p).GetClassID(out ret);
                }
                else if (p is IPersistFile)
                {
                    ((IPersistFile)p).GetClassID(out ret);
                }
                else if (p is IPersistMoniker)
                {
                    ((IPersistMoniker)p).GetClassID(out ret);
                }
                else if (p is IPersistStorage)
                {
                    ((IPersistStorage)p).GetClassID(out ret);
                }
            }
            catch
            {
            }

            return ret;
        }

        public static string GetMonikerDisplayName(IMoniker pmk)
        {
            string strDisplayName;
            IBindCtx bindCtx = CreateBindCtx(0);

            pmk.GetDisplayName(bindCtx, null, out strDisplayName);

            Marshal.ReleaseComObject(bindCtx);

            return strDisplayName;
        }

        public static byte[] MarshalObject(object obj, Guid iid, MSHCTX mshctx, MSHLFLAGS mshflags)
        {
            MemoryStream stm = new MemoryStream();
            CoMarshalInterface(new IStreamImpl(stm), ref iid, obj, mshctx, IntPtr.Zero, mshflags);
            return stm.ToArray();
        }

        public static byte[] MarshalObject(object obj)
        {
            return MarshalObject(obj, COMInterfaceEntry.IID_IUnknown, MSHCTX.DIFFERENTMACHINE, MSHLFLAGS.NORMAL);
        }

        public static COMObjRef MarshalObjectToObjRef(object obj, Guid iid, MSHCTX mshctx, MSHLFLAGS mshflags)
        {
            return COMObjRef.FromArray(MarshalObject(obj, iid, mshctx, mshflags));
        }

        public static COMObjRef MarshalObjectToObjRef(object obj)
        {
            return MarshalObjectToObjRef(obj, COMInterfaceEntry.IID_IUnknown, MSHCTX.DIFFERENTMACHINE, MSHLFLAGS.NORMAL);
        }

        private static string ConvertTypeToName(Type t)
        {
            if (t == typeof(string))
            {
                return "string";
            }
            else if (t == typeof(byte))
            {
                return "byte";
            }
            else if (t == typeof(sbyte))
            {
                return "sbyte";
            }
            else if (t == typeof(short))
            {
                return "short";
            }
            else if (t == typeof(ushort))
            {
                return "ushort";
            }
            else if (t == typeof(int))
            {
                return "int";
            }
            else if (t == typeof(uint))
            {
                return "uint";
            }
            else if (t == typeof(long))
            {
                return "long";
            }
            else if (t == typeof(ulong))
            {
                return "ulong";
            }
            else if (t == typeof(void))
            {
                return "void";
            }
            else if (t == typeof(object))
            {
                return "object";
            }
            else if (t == typeof(bool))
            {
                return "bool";
            }

            return t.Name;
        }

        public static string MemberInfoToString(MemberInfo member)
        {
            MethodInfo mi = member as MethodInfo;
            PropertyInfo prop = member as PropertyInfo;
            FieldInfo fi = member as FieldInfo;

            if (mi != null)
            {
                List<string> pars = new List<string>();
                ParameterInfo[] pis = mi.GetParameters();

                foreach (ParameterInfo pi in pis)
                {
                    List<string> dirs = new List<string>();

                    if (pi.IsOut)
                    {
                        dirs.Add("Out");
                        if (pi.IsIn)
                        {
                            dirs.Add("In");
                        }
                    }

                    if (pi.IsRetval)
                    {
                        dirs.Add("Retval");
                    }

                    if (pi.IsOptional)
                    {
                        dirs.Add("Optional");
                    }

                    string text = String.Format("{0} {1}", ConvertTypeToName(pi.ParameterType), pi.Name);

                    if (dirs.Count > 0)
                    {
                        text = String.Format("[{0}] {1}", string.Join(",", dirs), text);
                    }
                    pars.Add(text);
                }

                return String.Format("{0} {1}({2});", ConvertTypeToName(mi.ReturnType), mi.Name, String.Join(", ", pars));
            }
            else if (prop != null)
            {
                List<string> propdirs = new List<string>();
                if (prop.CanRead)
                {
                    propdirs.Add("get;");
                }

                if (prop.CanWrite)
                {
                    propdirs.Add("set;");
                }

                return String.Format("{0} {1} {{ {2} }}", ConvertTypeToName(prop.PropertyType), prop.Name, string.Join(" ", propdirs));
            }
            else if (fi != null)
            {
                return String.Format("{0} {1}", ConvertTypeToName(fi.FieldType), fi.Name);
            }
            else
            {
                return null;
            }
        }

        public static bool HasSubkey(this RegistryKey key, string name)
        {
            using (RegistryKey subkey = key.OpenSubKey(name))
            {
                return subkey != null;
            }
        }

        internal static int GetSafeHashCode<T>(this T obj) where T : class
        {
            if (obj == null)
            {
                return 0;
            }
            return obj.GetHashCode();
        }

        internal static int GetEnumHashCode<T>(this IEnumerable<T> e)
        {
            return e.Aggregate(0, (s, o) => s ^ o.GetHashCode());
        }

        internal static T[] EnumeratePointerList<T>(IntPtr p, Func<IntPtr, T> load_type)
        {
            List<T> ret = new List<T>();

            if (p == IntPtr.Zero)
            {
                return new T[0];
            }

            IntPtr curr = p;
            IntPtr value = IntPtr.Zero;
            while ((value = Marshal.ReadIntPtr(curr)) != IntPtr.Zero)
            {
                ret.Add(load_type(value));
                curr += IntPtr.Size;
            }
            return ret.ToArray();
        }

        internal static T[] EnumeratePointerList<T>(IntPtr p) where T : struct
        {
            return EnumeratePointerList(p, i => Marshal.PtrToStructure<T>(i));
        }

        internal static T[] ReadPointerArray<T>(IntPtr p, int count, Func<IntPtr, T> load_type)
        {
            T[] ret = new T[count];
            if (p == IntPtr.Zero)
            {
                return ret;
            }

            for (int i = 0; i < count; ++i)
            {
                IntPtr curr = Marshal.ReadIntPtr(p, i * IntPtr.Size);
                if (curr == IntPtr.Zero)
                {
                    ret[i] = default(T);
                }
                else
                {
                    ret[i] = load_type(curr);
                }
            }
            return ret;
        }

        internal static T[] ReadPointerArray<T>(IntPtr p, int count) where T : struct
        {
            return ReadPointerArray(p, count, i => Marshal.PtrToStructure<T>(i));
        }

        internal static Guid ReadGuid(IntPtr p)
        {
            if (p == IntPtr.Zero)
            {
                return COMInterfaceEntry.IID_IUnknown;
            }
            byte[] guid = new byte[16];
            Marshal.Copy(p, guid, 0, 16);
            return new Guid(guid);
        }

        internal static Guid ReadGuidFromArray(IntPtr p, int index)
        {
            if (p == IntPtr.Zero)
            {
                return Guid.Empty;
            }

            IntPtr guid_ptr = Marshal.ReadIntPtr(p, index * IntPtr.Size);
            return ReadGuid(guid_ptr);
        }

        internal static byte[] ReadAll(this BinaryReader reader, int length)
        {
            byte[] ret = reader.ReadBytes(length);
            if (ret.Length != length)
            {
                throw new EndOfStreamException();
            }
            return ret;
        }

        internal static Guid ReadGuid(this BinaryReader reader)
        {
            return new Guid(reader.ReadAll(16));
        }

        internal static char ReadUnicodeChar(this BinaryReader reader)
        {
            return BitConverter.ToChar(reader.ReadAll(2), 0);
        }

        internal static void Write(this BinaryWriter writer, Guid guid)
        {
            writer.Write(guid.ToByteArray());
        }

        internal static string ReadZString(this BinaryReader reader)
        {
            StringBuilder builder = new StringBuilder();
            char ch = reader.ReadUnicodeChar();
            while (ch != 0)
            {
                builder.Append(ch);
                ch = reader.ReadUnicodeChar();
            }
            return builder.ToString();
        }

        internal static void WriteZString(this BinaryWriter writer, string str)
        {
            writer.Write(Encoding.Unicode.GetBytes(str + "\0"));
        }

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern SafeLibraryHandle LoadLibrary(string filename);

        internal static SafeLibraryHandle SafeLoadLibrary(string filename)
        {
            SafeLibraryHandle lib = LoadLibrary(filename);
            if (lib.IsInvalid)
            {
                throw new Win32Exception();
            }
            return lib;
        }

        const int GET_MODULE_HANDLE_EX_FLAG_FROM_ADDRESS = 0x00000004;

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        static extern bool GetModuleHandleEx(int dwFlags, IntPtr lpModuleName, out SafeLibraryHandle phModule);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "GetModuleHandleExW")]
        static extern bool GetModuleHandleExString(int dwFlags, string lpModuleName, out SafeLibraryHandle phModule);

        internal static SafeLibraryHandle SafeGetModuleHandle(string name)
        {
            SafeLibraryHandle ret;
            if (!GetModuleHandleExString(0, name, out ret))
            {
                throw new Win32Exception();
            }
            return ret;
        }

        internal static SafeLibraryHandle SafeGetModuleHandle(IntPtr address)
        {
            SafeLibraryHandle ret;
            if (!GetModuleHandleEx(GET_MODULE_HANDLE_EX_FLAG_FROM_ADDRESS, address, out ret))
            {
                return null;
            }
            return ret;
        }

        // Walk series of jumps until we either find an address we don't know or we change modules.
        internal static IntPtr GetTargetAddress(SafeLibraryHandle curr_module, IntPtr ptr)
        {
            byte start_byte = Marshal.ReadByte(ptr);
            switch (start_byte)
            {
                // Absolute jump.
                case 0xFF:
                    if (Marshal.ReadByte(ptr + 1) != 0x25)
                    {
                        return ptr;
                    }

                    if (Environment.Is64BitProcess)
                    {
                        // RIP relative
                        ptr = Marshal.ReadIntPtr(ptr + 6 + Marshal.ReadInt32(ptr + 2));
                    }
                    else
                    {
                        // Absolute
                        ptr = Marshal.ReadIntPtr(new IntPtr(Marshal.ReadInt32(ptr + 2)));
                    }
                    break;
                // Relative jump.
                case 0xE9:
                    ptr = ptr + 5 + Marshal.ReadInt32(ptr + 1);
                    break;
                // lea rax, ofs import - Delay load 64bit
                case 0x48:
                    {
                        if (!Environment.Is64BitProcess || Marshal.ReadByte(ptr + 1) != 0x8D || Marshal.ReadByte(ptr + 2) != 0x05)
                        {
                            return ptr;
                        }
                        IntPtr iat = ptr + Marshal.ReadInt32(ptr + 3) + 7;
                        Dictionary<IntPtr, IntPtr> delayed_loaded = curr_module.ParseDelayedImports();
                        if (delayed_loaded.ContainsKey(iat))
                        {
                            return delayed_loaded[iat];
                        }
                        return ptr;
                    }
                // mov eax, ofs import - Delay load 32bit
                case 0xB8:
                    {
                        if (Environment.Is64BitProcess)
                        {
                            return ptr;
                        }
                        IntPtr iat = Marshal.ReadIntPtr(ptr + 1);
                        Dictionary<IntPtr, IntPtr> delayed_loaded = curr_module.ParseDelayedImports();
                        if (delayed_loaded.ContainsKey(iat))
                        {
                            return delayed_loaded[iat];
                        }
                        return ptr;
                    }
                default:
                    return ptr;
            }

            using (SafeLibraryHandle lib = COMUtilities.SafeGetModuleHandle(ptr))
            {
                if (lib == null || lib.DangerousGetHandle() != curr_module.DangerousGetHandle())
                {
                    return ptr;
                }
            }

            return GetTargetAddress(curr_module, ptr);
        }
        
        private static string GetNextToken(string name, out string token)
        {
            token = null;
            if (name.Length == 0)
            {
                return name;
            }
            int end_index = name.IndexOf('_');
            if (end_index < 0)
            {
                token = name;
            }
            else
            {
                token = name.Substring(0, end_index);
            }
            return name.Substring(end_index + 1).TrimStart('_');
        }

        private static string GetNextToken(string name, out int token)
        {
            if (name.Length == 0 || !char.IsDigit(name[0]))
            {
                throw new InvalidDataException("Expected an integer");
            }
            int length = 0;
            while (char.IsDigit(name[length]))
            {
                length++;
            }

            token = int.Parse(name.Substring(0, length));

            return name.Substring(length).TrimStart('_'); 
        }

        private static string ReadType(ref string name)
        {
            string token;
            name = GetNextToken(name, out token);
            if (String.IsNullOrEmpty(token))
            {
                throw new InvalidDataException("Expected a type name");
            }

            if (char.IsLetter(token[0]))
            {
                return token;
            }
            else if (token[0] == '~')
            {
                StringBuilder builder = new StringBuilder();
                int type_count;

                name = GetNextToken(name, out type_count);
                builder.Append(token.Substring(1));
                builder.Append("<");
                List<string> types = new List<string>();
                for (int i = 0; i < type_count; ++i)
                {
                    types.Add(ReadType(ref name));
                }
                builder.Append(String.Join(",", types));
                builder.Append(">");
                return builder.ToString();
            }
            else
            {
                throw new InvalidDataException("Expected a type name or a generic type");
            }
        }

        private static string DemangleGenericType(string name)
        {
            name = name.Replace("__F", "~").Replace("__C", "::");
            return ReadType(ref name);
        }

        // TODO: This isn't exactly correct, but can't find any good documentation.
        internal static string DemangleWinRTName(string name)
        {
            name = name.Trim();
            if (name.StartsWith("__x_"))
            {
                return name.Substring(4).Replace("_", "::");
            }

            if (name.StartsWith("__F"))
            {
                try
                {
                    return DemangleGenericType(name);
                }
                catch (InvalidDataException)
                {
                    System.Diagnostics.Debug.WriteLine(name);
                }
            }

            return name;
        }

        internal static COMRegistry LoadRegistry(IWin32Window window,
            Func<IProgress<Tuple<string, int>>, CancellationToken, object> worker)
        {
            using (WaitingDialog loader = new WaitingDialog(worker))
            {
                if (loader.ShowDialog(window) == DialogResult.OK)
                {
                    return loader.Result as COMRegistry;
                }
                else
                {
                    throw loader.Error;
                }
            }
        }

        internal static COMRegistry LoadRegistry(IWin32Window window, COMRegistryMode mode)
        {
            if (mode == COMRegistryMode.Diff)
            {
                throw new ArgumentException("Can't load a diff registry");
            }
            return LoadRegistry(window, (progress, token) => COMRegistry.Load(mode, null, progress));
        }

        internal static COMRegistry LoadRegistry(IWin32Window window, string database_file)
        {
            return LoadRegistry(window, (progress, token) => COMRegistry.Load(database_file, progress));
        }

        internal static COMRegistry DiffRegistry(IWin32Window window, COMRegistry left, COMRegistry right, COMRegistryDiffMode mode)
        {
            return LoadRegistry(window, (progress, token) => COMRegistry.Diff(left, right, mode, progress));
        }

        internal static Assembly LoadTypeLib(IWin32Window window, string path)
        {
            using (WaitingDialog dlg = new WaitingDialog((progress, token) => COMUtilities.LoadTypeLib(path, progress), s => s))
            {
                dlg.Text = String.Format("Loading TypeLib {0}", path);
                dlg.CancelEnabled = false;
                if (dlg.ShowDialog(window) == DialogResult.OK)
                {
                    return (Assembly)dlg.Result;
                }
                else if ((dlg.Error != null) && !(dlg.Error is OperationCanceledException))
                {
                    Program.ShowError(window, dlg.Error);
                }
                return null;
            }
        }

        internal static Assembly LoadTypeLib(IWin32Window window, ITypeLib typelib)
        {
            using (WaitingDialog dlg = new WaitingDialog((progress, token) => COMUtilities.LoadTypeLib(typelib, progress), s => s))
            {
                dlg.Text = "Loading TypeLib";
                dlg.CancelEnabled = false;
                if (dlg.ShowDialog(window) == DialogResult.OK)
                {
                    return (Assembly)dlg.Result;
                }
                else if ((dlg.Error != null) && !(dlg.Error is OperationCanceledException))
                {
                    Program.ShowError(window, dlg.Error);
                }
                return null;
            }
        }

        internal static IEnumerable<COMProcessEntry> LoadProcesses(IWin32Window window)
        {
            string dbghelp = Environment.Is64BitProcess
                    ? Properties.Settings.Default.DbgHelpPath64
                    : Properties.Settings.Default.DbgHelpPath32;
            string symbol_path = Properties.Settings.Default.SymbolPath;
            using (WaitingDialog dlg = new WaitingDialog((progress, token) => COMProcessParser.GetProcesses(dbghelp, symbol_path, progress), s => s))
            {
                dlg.Text = "Loading Processes";
                if (dlg.ShowDialog(window) == DialogResult.OK)
                {
                    return (IEnumerable<COMProcessEntry>)dlg.Result;
                }
                else if ((dlg.Error != null) && !(dlg.Error is OperationCanceledException))
                {
                    Program.ShowError(window, dlg.Error);
                }
                return null;
            }
        }

        private class ReportQueryProgress
        {
            private int _total_count;
            private int _current;
            private IProgress<Tuple<string, int>> _progress;

            const int MINIMUM_REPORT_SIZE = 25;

            public ReportQueryProgress(IProgress<Tuple<string, int>> progress, int total_count)
            {
                _total_count = total_count;
                _progress = progress;
            }

            public void Report()
            {
                int current = Interlocked.Increment(ref _current);
                if ((current % MINIMUM_REPORT_SIZE) == 1)
                {
                    _progress.Report(new Tuple<string, int>(String.Format("Querying Interfaces: {0} of {1}", current, _total_count),
                        (100 * current) / _total_count));
                }
            }
        }

        private static bool QueryAllInterfaces(IEnumerable<COMCLSIDEntry> clsids, IProgress<Tuple<string, int>> progress, CancellationToken token, int concurrent_queries)
        {
            ParallelOptions po = new ParallelOptions();
            po.CancellationToken = token;
            po.MaxDegreeOfParallelism = concurrent_queries;

            ReportQueryProgress query_progress = new ReportQueryProgress(progress, clsids.Count());

            Parallel.ForEach(clsids, po, clsid =>
            {
                po.CancellationToken.ThrowIfCancellationRequested();
                try
                {
                    query_progress.Report();
                    clsid.LoadSupportedInterfaces(false);
                }
                catch
                {
                }
            });

            return true;
        }

        internal static bool QueryAllInterfaces(IWin32Window parent, IEnumerable<COMCLSIDEntry> clsids, IEnumerable<COMServerType> server_types, int concurrent_queries, bool refresh_interfaces)
        {
            using (WaitingDialog dlg = new WaitingDialog(
                (p, t) => COMUtilities.QueryAllInterfaces(clsids.Where(c => (refresh_interfaces || !c.InterfacesLoaded) && server_types.Contains(c.DefaultServerType)),
                            p, t, concurrent_queries),
                s => s))
            {
                dlg.Text = "Querying Interfaces";
                return dlg.ShowDialog(parent) == DialogResult.OK;
            }
        }

        internal static string FormatGuid(this Guid guid)
        {
            return guid.ToString(Properties.Settings.Default.GuidFormat).ToUpper();
        }

        internal static string FormatGuidDefault(this Guid guid)
        {
            return guid.ToString().ToUpper();
        }

        [StructLayout(LayoutKind.Sequential)]
        struct SERVICE_STATUS_PROCESS
        {
            public int dwServiceType;
            public int dwCurrentState;
            public int dwControlsAccepted;
            public int dwWin32ExitCode;
            public int dwServiceSpecificExitCode;
            public int dwCheckPoint;
            public int dwWaitHint;
            public int dwProcessId;
            public int dwServiceFlags;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct ENUM_SERVICE_STATUS_PROCESS
        {
            public IntPtr lpServiceName;
            public IntPtr lpDisplayName;
            public SERVICE_STATUS_PROCESS ServiceStatusProcess;

            public string GetName()
            {
                if (lpServiceName != IntPtr.Zero)
                {
                    return Marshal.PtrToStringUni(lpServiceName);
                }
                return String.Empty;
            }
        }

        const int SC_MANAGER_CONNECT = 0x0001;
        const int SC_MANAGER_ENUMERATE_SERVICE = 0x0004;
        const int SERVICE_WIN32 = 0x00000030;
        const int SERVICE_ACTIVE = 0x00000001;
        const int ERROR_MORE_DATA = 234;

        [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        static extern IntPtr OpenSCManager(
              string lpMachineName,
              string lpDatabaseName,
              int dwDesiredAccess
            );

        [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        static extern bool CloseServiceHandle(
                  IntPtr hSCObject
                );

        [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        static extern bool EnumServicesStatusEx(
              IntPtr hSCManager,
              int InfoLevel,
              int dwServiceType,
              int dwServiceState,
              SafeBuffer lpServices,
              int cbBufSize,
              out int pcbBytesNeeded,
              out int lpServicesReturned,
              IntPtr lpResumeHandle,
              string pszGroupName
            );

        internal static Dictionary<int, HashSet<string>> GetServicePids()
        {
            Dictionary<int, HashSet<string>> ret = new Dictionary<int, HashSet<string>>();
            IntPtr hSC = OpenSCManager(null, null, SC_MANAGER_CONNECT | SC_MANAGER_ENUMERATE_SERVICE);
            try
            {
                int bytes_needed = 0;
                int service_count = 0;
                EnumServicesStatusEx(hSC, 0, SERVICE_WIN32, SERVICE_ACTIVE, SafeHGlobalBuffer.Null, 0, out bytes_needed, out service_count, IntPtr.Zero, null);
                if (Marshal.GetLastWin32Error() != ERROR_MORE_DATA || bytes_needed <= 0)
                {
                    return ret;
                }

                int repeat_count = 5;
                while (repeat_count > 0)
                {
                    using (SafeHGlobalBuffer buf = new SafeHGlobalBuffer(bytes_needed))
                    {
                        if (EnumServicesStatusEx(hSC, 0, SERVICE_WIN32, SERVICE_ACTIVE, buf, buf.Length, out bytes_needed, out service_count, IntPtr.Zero, null))
                        {
                            ENUM_SERVICE_STATUS_PROCESS[] services = new ENUM_SERVICE_STATUS_PROCESS[service_count];
                            buf.ReadArray(0, services, 0, service_count);
                            foreach (var service in services)
                            {
                                string name = service.GetName();
                                if (!String.IsNullOrWhiteSpace(name))
                                {
                                    if (!ret.ContainsKey(service.ServiceStatusProcess.dwProcessId))
                                    {
                                        ret[service.ServiceStatusProcess.dwProcessId] = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                                    }

                                    ret[service.ServiceStatusProcess.dwProcessId].Add(name);
                                }
                            }
                        }
                        --repeat_count;
                    }
                }
            }
            finally
            {
                if (hSC != IntPtr.Zero)
                {
                    CloseServiceHandle(hSC);
                }
            }

            return ret;
        }

        internal static bool IsAdministrator()
        {
            using (WindowsIdentity id = WindowsIdentity.GetCurrent())
            {
                return new WindowsPrincipal(id).IsInRole(WindowsBuiltInRole.Administrator);
            }
        }

        internal static string GetCOMDllName()
        {
            if (File.Exists(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "combase.dll")))
            {
                return "combase";
            }
            else
            {
                return "ole32";
            }
        }

        internal static int GetProcessIdFromIPid(Guid ipid)
        {
            return BitConverter.ToUInt16(ipid.ToByteArray(), 4);
        }

        internal static int GetApartmentIdFromIPid(Guid ipid)
        {
            return BitConverter.ToInt16(ipid.ToByteArray(), 6);
        }

        internal static string GetApartmentIdStringFromIPid(Guid ipid)
        {
            int appid = GetApartmentIdFromIPid(ipid);
            switch (appid)
            {
                case 0:
                    return "NTA";
                case -1:
                    return "MTA";
                default:
                    return String.Format("STA (Thread ID {0})", appid);
            }
        }
    }

    internal class SafeLibraryHandle : SafeHandleZeroOrMinusOneIsInvalid
    {
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool FreeLibrary(IntPtr hModule);

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Ansi)]
        private static extern IntPtr GetProcAddress(IntPtr hModule, string name);

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Ansi, EntryPoint = "GetProcAddress")]
        private static extern IntPtr GetProcAddressOrdinal(IntPtr hModule, IntPtr name);

        internal SafeLibraryHandle(IntPtr ptr, bool ownsHandle) : base(ownsHandle)
        {
        }

        private SafeLibraryHandle() : base(true)
        {
        }

        protected override bool ReleaseHandle()
        {
            bool ret = true;
            if (handle != IntPtr.Zero)
            {
                ret = FreeLibrary(handle);
                handle = IntPtr.Zero;
            }
            return ret;
        }

        public TDelegate GetFunctionPointer<TDelegate>() where TDelegate : class
        {
            if (!typeof(TDelegate).IsSubclassOf(typeof(Delegate)) || 
                typeof(TDelegate).GetCustomAttribute<UnmanagedFunctionPointerAttribute>() == null)
            {
                throw new ArgumentException("Invalid delegate type, must have an UnmanagedFunctionPointerAttribute annotation");
            }

            IntPtr proc = GetFunctionPointer(typeof(TDelegate).Name);
            if (proc == IntPtr.Zero)
            {
                throw new Win32Exception();
            }

            return Marshal.GetDelegateForFunctionPointer<TDelegate>(GetFunctionPointer(typeof(TDelegate).Name));
        }

        public IntPtr GetFunctionPointer(string name)
        {
            return GetProcAddress(handle, name);
        }

        public IntPtr GetFunctionPointer(IntPtr ordinal)
        {
            return GetProcAddressOrdinal(handle, ordinal);
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        static extern int GetModuleFileName(IntPtr hModule, StringBuilder lpFilename, int nSize);

        internal string GetModuleFileName()
        {
            StringBuilder builder = new StringBuilder(260);
            int result = GetModuleFileName(handle, builder, builder.Capacity);
            if (result > 0)
            {
                string path = builder.ToString();
                int index = path.LastIndexOf('\\');
                if (index < 0)
                    index = path.LastIndexOf('/');
                if (index < 0)
                {
                    return path;
                }
                return path.Substring(index + 1);
            }
            return "Unknown";
        }

        [DllImport("dbghelp.dll", SetLastError = true)]
        static extern IntPtr ImageDirectoryEntryToData(IntPtr Base, bool MappedAsImage, ushort DirectoryEntry, out int Size);

        const ushort IMAGE_DIRECTORY_ENTRY_DELAY_IMPORT = 13;

        private IntPtr RvaToVA(long rva)
        {
            return new IntPtr(handle.ToInt64() + rva);
        }

        [StructLayout(LayoutKind.Sequential)]
        struct IMAGE_DELAY_IMPORT_DESCRIPTOR
        {
            public uint Characteristics;
            public int szName;
            public int phmod;
            public int pIAT;
            public int pINT;
            public int pBoundIAT;
            public int pUnloadIAT;
            public uint dwTimeStamp;
        }

        private void ParseDelayedImport(Dictionary<IntPtr, IntPtr> imports, IMAGE_DELAY_IMPORT_DESCRIPTOR desc)
        {
            if (desc.pIAT == 0 || desc.pINT == 0)
            {
                return;
            }

            string name = Marshal.PtrToStringAnsi(RvaToVA(desc.szName));
            IntPtr IAT = RvaToVA(desc.pIAT);
            IntPtr INT = RvaToVA(desc.pINT);

            try
            {
                using (SafeLibraryHandle lib = COMUtilities.SafeLoadLibrary(name))
                {
                    IntPtr import_name_rva = Marshal.ReadIntPtr(INT);

                    while (import_name_rva != IntPtr.Zero)
                    {
                        IntPtr import;
                        // Ordinal
                        if (import_name_rva.ToInt64() < 0)
                        {
                            import = lib.GetFunctionPointer(new IntPtr(import_name_rva.ToInt64() & 0xFFFF));
                        }
                        else
                        {
                            IntPtr import_ofs = RvaToVA(import_name_rva.ToInt64() + 2);
                            string import_name = Marshal.PtrToStringAnsi(import_ofs);
                            import = lib.GetFunctionPointer(import_name);
                        }

                        if (import != IntPtr.Zero)
                        {
                            imports[IAT] = import;
                        }

                        INT += IntPtr.Size;
                        IAT += IntPtr.Size;
                        import_name_rva = Marshal.ReadIntPtr(INT);
                    }
                }
            }
            catch (Win32Exception)
            {
            }
        }

        private Dictionary<IntPtr, IntPtr> _delayed_imports;

        public Dictionary<IntPtr, IntPtr> ParseDelayedImports()
        {
            if (_delayed_imports != null)
            {
                return _delayed_imports;
            }
            _delayed_imports = new Dictionary<IntPtr, IntPtr>();
            int size;
            IntPtr delayed_imports = ImageDirectoryEntryToData(handle, true, IMAGE_DIRECTORY_ENTRY_DELAY_IMPORT, out size);
            if (delayed_imports == null)
            {
                return _delayed_imports;
            }

            int i = 0;
            int desc_size = Marshal.SizeOf(typeof(IMAGE_DELAY_IMPORT_DESCRIPTOR));
            // Should really only do up to sizeof image delay import desc
            while (i <= (size - desc_size))
            {
                IMAGE_DELAY_IMPORT_DESCRIPTOR desc = Marshal.PtrToStructure<IMAGE_DELAY_IMPORT_DESCRIPTOR>(delayed_imports);
                if (desc.szName == 0)
                {
                    break;
                }

                ParseDelayedImport(_delayed_imports, desc);

                delayed_imports += desc_size;
                size -= desc_size;
            }

            return _delayed_imports;
        }
    }

}
