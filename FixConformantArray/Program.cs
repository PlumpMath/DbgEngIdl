using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace FixConformantArray
{
    class Program
    {
        static void Main( string[] args )
        {
            Console.WriteLine("Generating IDL..");
            DbgEngIdl.Program.Main(null);

            Console.WriteLine("Dassembling..");
            Process.Start("dasm.bat").WaitForExit();

            Console.WriteLine("Fixing..");
            Fix("_Interop.DbgEng.il", "DbgEng.idl");

            Console.WriteLine("Assembling..");
            Process.Start("asm.bat").WaitForExit();
        }

        const string IlInterfaceStart = ".class interface ";
        const string IlMethodStart = "          instance void  ";

        private static void Fix( string ilFile, string idlFile )
        {
            var ils = File.ReadAllLines(ilFile);
            var idls = File.ReadAllLines(idlFile);

            var arrayParams = AnalyzeIdl(idls);

            using ( var output = new StreamWriter(ilFile.Substring(1), false) )
            {
                InspectIL(ils, arrayParams, output);
            }
        }

        private static void InspectIL( string[] ils, Dictionary<string, int> arrayParams, StreamWriter output )
        {
            string currentInterface = null;
            for ( int ilLine = 0; ilLine < ils.Length; ilLine++ )
            {
                var il = ils[ilLine];
                if ( il.StartsWith(IlInterfaceStart) )
                {
                    currentInterface = il.Substring(il.LastIndexOf('.') + 1);
                    output.WriteLine(il);
                }
                else if ( il.StartsWith(IlMethodStart) )
                {
                    InspectIlMethod(output, arrayParams, currentInterface, il, ils, ref ilLine);
                }
                else
                {
                    output.WriteLine(il);
                }
            }
        }

        private static void InspectIlMethod( TextWriter output
                                           , Dictionary<string, int> arrayParams
                                           , string currentInterface
                                           , string il
                                           , string[] ils
                                           , ref int ilLine
                                           )
        {
            var L = il.IndexOf('(') + 1;
            var methodName = il.Substring(IlMethodStart.Length, L - IlMethodStart.Length - 1);
            for ( ; !(il = ils[ilLine]).EndsWith("{"); ilLine++ )
            {
                var paramDef = il.Substring(L);
                var parts = paramDef.Split(' ');

                if ( parts[1] == "string" || parts[2].EndsWith(".StringBuilder") || parts[1].EndsWith("[]") )
                {
                    output.WriteLine(il);
                    continue;
                }
                var paramName = parts[2].Remove(parts[2].Length - 1);
                var paramKey = GetArrayParamKey(currentInterface, methodName, paramName);
                if ( !arrayParams.ContainsKey(paramKey) )
                {
                    output.WriteLine(il);
                    continue;
                }

                parts[1] = parts[1].Replace("&", "[] marshal([ + " + arrayParams[paramKey] + "])");
                output.Write(il.Substring(0, L));
                output.WriteLine(String.Join(" ", parts));
            }

            output.WriteLine(il);
        }

        private static string GetArrayParamKey( string interfaceName, string methodName, string paramName )
        {
            return String.Format("{0}.{1}.{2}", interfaceName, methodName, paramName);
        }

        const string IdlInterfaceStart = "interface ";
        const string IdlMethodStart = "    HRESULT ";
        const string IdlMethodEnd = "    );";

        private static Dictionary<string, int> AnalyzeIdl( string[] idls )
        {
            var arrayParamNames = new Dictionary<string, int>();

            string currentInterface = null;
            for ( int idlLine = 0; idlLine < idls.Length; idlLine++ )
            {
                var idl = idls[idlLine];
                if ( idl.StartsWith(IdlInterfaceStart) )
                {
                    currentInterface = idl.Split(' ')[1];
                }
                else if ( idl.StartsWith(IdlMethodStart) )
                {
                    CollectArrayParamNames(arrayParamNames, currentInterface, idl, idls, ref idlLine);
                }
            }

            return arrayParamNames;
        }

        static char[] IdlElmCountRSep = new[] { ')', ' ' };

        private static void CollectArrayParamNames( Dictionary<string, int> arrayParamNames
                                                  , string currentInterface
                                                  , string idl
                                                  , string[] idls
                                                  , ref int idlLine
                                                  )
        {
            var methodName = idl.Substring(IdlMethodStart.Length, idl.IndexOf('(') - IdlMethodStart.Length);
            idlLine++;

            var allParams = new List<string>();
            var arrayParams = new List<KeyValuePair<string, string>>();
            for ( ; !(idl = idls[idlLine]).StartsWith(IdlMethodEnd); idlLine++ )
            {
                var l = idl.LastIndexOf(' ') + 1;
                var paramName = idl.Substring(l).Replace("[]", "").Replace(",", "");

                allParams.Add(paramName);

                if ( idl.Contains("size_is") )
                {
                    l = idl.IndexOf('(') + 1;
                    var eCountParam = idl.Substring(l, idl.IndexOfAny(IdlElmCountRSep, l) - l);
                    arrayParams.Add(new KeyValuePair<string, string>(paramName, eCountParam));
                }
            }

            foreach ( var param in arrayParams )
            {
                arrayParamNames.Add(GetArrayParamKey(currentInterface, methodName, param.Key), allParams.IndexOf(param.Value));
            }
        }
    }
}
