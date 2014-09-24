using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace DbgEngIdl
{
    class Program
    {
        static void Main( string[] args )
        {
            var header = File.ReadAllText("header.idl");
            var footer = File.ReadAllText("footer.idl");
            var hpp = File.ReadAllLines("dbgeng.h");

            File.WriteAllText("DbgEng.idl", GenerateIdl(header, footer, hpp));
        }

        private static string GenerateIdl( string header, string footer, string[] hpp )
        {
            var output = new StringBuilder(512000);

            output.Append(header);

            Dictionary<string, string> intDefs;
            var lineNumber = ExtractForwardDefinitions(output, out intDefs, hpp);

            ExtractDefinitions(output, intDefs, hpp, lineNumber);

            output.Append(footer);
            return output.ToString();
        }

        private static int ExtractForwardDefinitions( StringBuilder output, out Dictionary<string, string> intDefs, string[] hpp )
        {
            output.AppendLine("//// Interface forward definitions");

            intDefs = new Dictionary<string, string>();
            var signature = "typedef interface DECLSPEC_UUID(\"";

            var found = false;
            int i;
            for ( i = 0; i < hpp.Length; i++ )
            {
                var line = hpp[i];
                if ( line.StartsWith(signature) )
                {
                    found = true;
                    var guid = line.Substring(signature.Length, "f2df5f53-071f-47bd-9de6-5734c3fed689".Length);
                    var typedef = hpp[i + 1].Trim();
                    var name = typedef.Substring(0, typedef.IndexOf('*'));

                    intDefs.Add(name, guid);

                    output.AppendLine();
                    output.AppendLine("interface " + name + ";");
                    output.AppendLine("typedef " + typedef);

                    i++;
                }

                if ( found && line.StartsWith("//--") )
                {
                    break;
                }
            }

            output.AppendLine();
            return i;
        }

        private static void ExtractDefinitions( StringBuilder output, Dictionary<string, string> intDefs, string[] hpp, int i )
        {
            var constants = new List<KeyValuePair<string, string>>();

            for ( ; i < hpp.Length; i++ )
            {
                var line = hpp[i];
                if ( line.StartsWith("#define ") )
                {
                    string key, value;
                    if ( CollectConstant(line, out key, out value) )
                    {
                        constants.Add(new KeyValuePair<string, string>(key, value));
                    }
                }
                else if ( line.StartsWith("typedef struct ") )
                {
                    i = WriteStructTypeDef(output, hpp, i);
                }
                else if ( line.StartsWith("typedef union") )
                {
                    i = WriteStructTypeDef(output, hpp, i, true);
                }
                else if ( line.StartsWith("DECLARE_INTERFACE_") )
                {
                    i = WriteInterfaceDef(output, intDefs, hpp, i);
                }
            }

            foreach ( var constant in constants )
            {
                output.AppendFormat("#define {0} {1}", constant.Key, constant.Value).AppendLine();
            }
        }

        private static string CamelCase( string str )
        {
            var parts = str.ToLower()
                           .Split('_')
                           .Select(s => String.Format("{0}{1}", Char.ToUpper(s[0]), s.Length == 1 ? "" : s.Substring(1)));
            return String.Join("", parts);
        }

        private static bool CollectConstant( string line, out string key, out string value )
        {
            key = value = null;
            var parts = line.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if ( !Char.IsDigit(parts[2][0]) )
            {
                return false;
            }
            else
            {
                key = parts[1];
                value = parts[2];
                return true;
            }
        }

        private static int WriteStructTypeDef( StringBuilder output, string[] hpp, int i, bool isUnion = false )
        {
            var line = hpp[i].Trim();
            output.AppendLine(line);

            var structName = line.Substring(String.Format("typedef {0} _", isUnion ? "union" : "struct").Length);
            if ( structName.EndsWith("{") )
            {
                structName = structName.Remove(structName.IndexOf(' '));
                i++;
            }
            else
            {
                output.AppendLine("{");
                i += 2;
            }

            var endSignature = "} " + structName;

            for ( ; !(line = hpp[i].Trim()).StartsWith(endSignature); i++ )
            {
                if ( line.StartsWith("struct")
                  || line.StartsWith("union")
                  || line.StartsWith("{")
                  || line.StartsWith("}")
                  || line.StartsWith("//")
                  || String.IsNullOrEmpty(line)
                   )
                {
                    output.AppendLine(line);
                }
                else
                {
                    var sep = line.IndexOf(' ');
                    var type = line.Substring(0, sep);
                    output.Append("    ");
                    if ( type == "IN" || type == "OUT" )
                    {
                        output.Append('[').Append(type.ToLower()).Append("] ");

                        sep = line.IndexOf(' ', sep + 1);
                        var used = type.Length + 1;
                        type = line.Substring(used, sep - used);
                    }
                    output.Append(ToIdlType(type));
                    output.AppendLine(line.Substring(sep));
                }
            }

            output.AppendLine(line).AppendLine();
            return i;
        }

        private static int WriteInterfaceDef( StringBuilder output, Dictionary<string, string> intDefs, string[] hpp, int i )
        {
            var signature = "DECLARE_INTERFACE_(";
            var line = hpp[i];
            var name = line.Substring(signature.Length, line.IndexOf(',') - signature.Length);
            var super = line.Substring(line.IndexOf(',') + 1);
            super = super.Substring(0, super.Length - 1).Trim();

            output.AppendLine("[")
                  .AppendLine("    object,")
                  .AppendLine("    uuid(" + intDefs[name] + "),")
                  .AppendLine("    helpstring(\"" + name + "\")")
                  .AppendLine("]")
                  .AppendFormat("interface {0} : {1} ", name, super).AppendLine()
                  .AppendLine("{")
                  ;

            var methodStart = "STDMETHOD";
            var inMethod = false;
            for ( ; (line = hpp[i].Trim()) != "};"; i++ )
            {
                if ( !inMethod && line.StartsWith(methodStart) )
                {
                    var L = line.IndexOf('(') + 1;
                    var R = line.IndexOf(')');
                    var methodName = line.Substring(L, R - L);
                    if ( methodName == "QueryInterface" )
                    {
                        i += 10;
                    }
                    else
                    {
                        output.Append("    HRESULT ").Append(methodName).AppendLine("(");
                        inMethod = true;
                    }
                }
                else if ( inMethod && line.StartsWith("_") )
                {
                    line = Regex.Replace(line, @" *?/\*.*?\*/ *", " ");
                    var parts = line.Split(' ');
                    output.Append("        ");
                    if ( parts[1] == "_Reserved_" )
                    {
                        parts[1] = parts[2];
                        parts[2] = parts[3];
                    }
                    output.Append(ToIdlAttr(parts[0])).Append(' ')
                          .Append(ToIdlType(parts[1])).Append(' ')
                          .AppendLine(parts[2]);
                }
                else if ( inMethod && line.StartsWith(".") )
                {
                    output.AppendLine("        SAFEARRAY(VARIANT)");
                }
                else if ( inMethod && line.StartsWith(")") )
                {
                    output.AppendLine("    );");
                    inMethod = false;
                }
            }

            output.AppendLine("};").AppendLine();
            return i;
        }

        private static string ToIdlAttr( string cppAttr )
        {
            var result = new StringBuilder("[");

            if ( cppAttr.StartsWith("_In_") )
            {
                result.Append("in");
            }
            else if ( cppAttr.StartsWith("_Out_") )
            {
                result.Append("out");
            }
            else
            {
                result.Append("in,out");
            }

            if ( cppAttr.Contains("_opt_") )
            {
                result.Append(",optional");
            }

            var lp = cppAttr.IndexOf('(');
            if ( lp > 0 )
            {
                var param = cppAttr.Substring(lp + 1, cppAttr.Length - lp - 2);
                if ( cppAttr.Contains("_to_") )
                {
                    param = param.Split(',')[0];
                }
                result.AppendFormat(",{0}_is({1})", (cppAttr.Contains("_bytes_") ? "size" : "length"), param);
            }

            return result.Append(']').ToString();
        }

        private static string ToIdlType( string cppType )
        {
            return cppType;
        }
    }
}
