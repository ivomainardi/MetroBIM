using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Security.Cryptography;
using System.Text;

namespace MetroSP.Alinhamento
{
    /// <summary>
    /// Grava alinhamentos num arquivo IFC 4.3 (IFC4X3_ADD2), com:
    ///  - semântica completa: IfcAlignment > IfcAlignmentHorizontal + IfcAlignmentVertical, com os segmentos
    ///    (retas, arcos, espirais, rampas e curvas verticais) e seus parâmetros de projeto;
    ///  - geometria: eixo 3D (Axis/Curve3D) amostrado no Civil 3D, com as cotas do perfil.
    /// </summary>
    public class IfcAlignmentWriter
    {
        readonly List<string> _data = new List<string>();
        readonly IfcExportInfo _info;
        int _next = 1;

        public IfcAlignmentWriter(IfcExportInfo info) { _info = info; }

        public void Write(string path, IList<AlignmentModel> alignments)
        {
            // ------------------------------------------------ projeto, unidades, contexto
            var origin3 = Add("IFCCARTESIANPOINT((0.,0.,0.))");
            var world = Add($"IFCAXIS2PLACEMENT3D({R(origin3)},$,$)");
            var ctx = Add($"IFCGEOMETRICREPRESENTATIONCONTEXT($,'Model',3,1.E-05,{R(world)},$)");
            var axisCtx = Add($"IFCGEOMETRICREPRESENTATIONSUBCONTEXT('Axis','Model',*,*,*,*,{R(ctx)},$,.MODEL_VIEW.,$)");

            var units = new[]
            {
                Add("IFCSIUNIT(*,.LENGTHUNIT.,$,.METRE.)"),
                Add("IFCSIUNIT(*,.AREAUNIT.,$,.SQUARE_METRE.)"),
                Add("IFCSIUNIT(*,.VOLUMEUNIT.,$,.CUBIC_METRE.)"),
                Add("IFCSIUNIT(*,.PLANEANGLEUNIT.,$,.RADIAN.)")
            };
            var unitAssignment = Add($"IFCUNITASSIGNMENT({List(units)})");
            var project = Add($"IFCPROJECT({G("project")},$,{S(_info.ProjectName)},$,$,$,$,({R(ctx)}),{R(unitAssignment)})");

            if (!string.IsNullOrWhiteSpace(_info.ProjectedCrs))
            {
                var crs = Add($"IFCPROJECTEDCRS({S(_info.ProjectedCrs)},$,$,$,$,$,$)");
                // Coordenadas gravadas já são as do sistema projetado: deslocamento zero, sem rotação.
                Add($"IFCMAPCONVERSION({R(ctx)},{R(crs)},0.,0.,0.,1.,0.,1.)");
            }

            var sitePlacement = Add($"IFCLOCALPLACEMENT($,{R(world)})");
            var site = Add($"IFCSITE({G("site")},$,'Site',$,$,{R(sitePlacement)},$,$,.ELEMENT.,$,$,$,$,$)");

            // ------------------------------------------------ alinhamentos
            var alignmentIds = new List<int>();
            foreach (var a in alignments)
                alignmentIds.Add(WriteAlignment(a, sitePlacement, axisCtx));

            Add($"IFCRELAGGREGATES({G("project-site")},$,$,$,{R(project)},({R(site)}))");
            if (alignmentIds.Count > 0)
            {
                // IFC 4.3: alinhamento agregado ao projeto e referenciado no terreno.
                Add($"IFCRELAGGREGATES({G("project-alignments")},$,$,$,{R(project)},{List(alignmentIds)})");
                Add($"IFCRELREFERENCEDINSPATIALSTRUCTURE({G("site-alignments")},$,$,$,{List(alignmentIds)},{R(site)})");
            }

            // ------------------------------------------------ arquivo
            var sb = new StringBuilder();
            sb.Append("ISO-10303-21;\n");
            sb.Append("HEADER;\n");
            sb.Append("FILE_DESCRIPTION(('ViewDefinition [Alignment-basedView]'),'2;1');\n");
            sb.Append($"FILE_NAME({S(Path.GetFileName(path))},{S(DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss"))},({S(_info.Author)}),('MetroSP'),'MetroSP','MetroSP Civil 3D','');\n");
            sb.Append("FILE_SCHEMA(('IFC4X3_ADD2'));\n");
            sb.Append("ENDSEC;\n");
            sb.Append("DATA;\n");
            foreach (var line in _data) sb.Append(line).Append('\n');
            sb.Append("ENDSEC;\n");
            sb.Append("END-ISO-10303-21;\n");
            File.WriteAllText(path, sb.ToString(), new UTF8Encoding(false));
        }

        int WriteAlignment(AlignmentModel a, int sitePlacement, int axisCtx)
        {
            var key = "al:" + a.Handle;
            var placement = Add($"IFCLOCALPLACEMENT({R(sitePlacement)},{R(Add("IFCAXIS2PLACEMENT3D(" + R(Add("IFCCARTESIANPOINT((0.,0.,0.))")) + ",$,$)"))})");

            // Geometria: eixo 3D com as cotas do perfil.
            string shape = "$";
            if (a.Axis3D.Count >= 2)
            {
                var coords = string.Join(",", a.Axis3D.Select(p => $"({F(p[0])},{F(p[1])},{F(p[2])})"));
                var pts = Add($"IFCCARTESIANPOINTLIST3D(({coords}),$)");
                var curve = Add($"IFCINDEXEDPOLYCURVE({R(pts)},$,.F.)");
                var rep = Add($"IFCSHAPEREPRESENTATION({R(axisCtx)},'Axis','Curve3D',({R(curve)}))");
                shape = R(Add($"IFCPRODUCTDEFINITIONSHAPE($,$,({R(rep)}))"));
            }

            var alignment = Add($"IFCALIGNMENT({G(key)},$,{S(a.Name)},{S(a.Description)},$,{R(placement)},{shape},.NOTDEFINED.)");
            var layouts = new List<int>();

            // ---------- horizontal
            var horizontal = Add($"IFCALIGNMENTHORIZONTAL({G(key + ":h")},$,{S(a.Name + " - Horizontal")},$,$,{R(placement)},$)");
            layouts.Add(horizontal);
            var hSegs = new List<int>();
            for (int i = 0; i < a.Horizontal.Count; i++)
            {
                var h = a.Horizontal[i];
                var p = Add($"IFCCARTESIANPOINT(({F(h.StartX)},{F(h.StartY)}))");
                var dp = Add($"IFCALIGNMENTHORIZONTALSEGMENT($,$,{R(p)},{F(h.StartDirection)},{F(h.StartRadius)},{F(h.EndRadius)},{F(h.Length)},$,.{h.Type}.)");
                hSegs.Add(Add($"IFCALIGNMENTSEGMENT({G(key + ":h" + i)},$,{S($"H{i + 1} {h.Type} est. {h.StartStation:0.000}")},$,$,{R(placement)},$,{R(dp)})"));
            }
            if (hSegs.Count > 0) Add($"IFCRELNESTS({G(key + ":hn")},$,$,$,{R(horizontal)},{List(hSegs)})");

            // ---------- vertical
            if (a.Vertical.Count > 0)
            {
                var vertical = Add($"IFCALIGNMENTVERTICAL({G(key + ":v")},$,{S(a.Name + " - Vertical (" + a.ProfileName + ")")},$,$,{R(placement)},$)");
                layouts.Add(vertical);
                var vSegs = new List<int>();
                for (int i = 0; i < a.Vertical.Count; i++)
                {
                    var v = a.Vertical[i];
                    var dp = Add($"IFCALIGNMENTVERTICALSEGMENT($,$,{F(v.StartDistAlong)},{F(v.HorizontalLength)},{F(v.StartHeight)},{F(v.StartGradient)},{F(v.EndGradient)},{(v.Radius == 0 ? "$" : F(v.Radius))},.{v.Type}.)");
                    vSegs.Add(Add($"IFCALIGNMENTSEGMENT({G(key + ":v" + i)},$,{S($"V{i + 1} {v.Type}")},$,$,{R(placement)},$,{R(dp)})"));
                }
                Add($"IFCRELNESTS({G(key + ":vn")},$,$,$,{R(vertical)},{List(vSegs)})");
            }

            Add($"IFCRELNESTS({G(key + ":n")},$,$,$,{R(alignment)},{List(layouts)})");

            // ---------- propriedades
            var props = new List<int>
            {
                Add($"IFCPROPERTYSINGLEVALUE('Alinhamento',$,IFCLABEL({S(a.Name)}),$)"),
                Add($"IFCPROPERTYSINGLEVALUE('Perfil',$,IFCLABEL({S(a.ProfileName ?? "")}),$)"),
                Add($"IFCPROPERTYSINGLEVALUE('EstacaInicial',$,IFCLENGTHMEASURE({F(a.StartStation)}),$)"),
                Add($"IFCPROPERTYSINGLEVALUE('EstacaFinal',$,IFCLENGTHMEASURE({F(a.EndStation)}),$)"),
                Add($"IFCPROPERTYSINGLEVALUE('ArquivoOrigem',$,IFCLABEL({S(_info.SourceFile)}),$)")
            };
            var pset = Add($"IFCPROPERTYSET({G(key + ":pset")},$,'MetroSP_Alinhamento',$,{List(props)})");
            Add($"IFCRELDEFINESBYPROPERTIES({G(key + ":pdef")},$,$,$,({R(alignment)}),{R(pset)})");

            return alignment;
        }

        // ------------------------------------------------------------------ STEP

        int Add(string entity)
        {
            var id = _next++;
            _data.Add("#" + id + "=" + entity + ";");
            return id;
        }

        static string R(int id) => "#" + id;
        static string List(IEnumerable<int> ids) => "(" + string.Join(",", ids.Select(R)) + ")";

        /// <summary>Real no formato STEP: sempre com ponto decimal (1. / 1.5 / 1.E-05).</summary>
        public static string F(double v)
        {
            if (double.IsNaN(v) || double.IsInfinity(v)) throw new ArgumentException("Valor inválido para o IFC: " + v);
            var s = v.ToString("R", CultureInfo.InvariantCulture);
            var e = s.IndexOfAny(new[] { 'E', 'e' });
            var mantissa = e < 0 ? s : s.Substring(0, e);
            var exp = e < 0 ? "" : "E" + s.Substring(e + 1);
            if (!mantissa.Contains('.')) mantissa += ".";
            return mantissa + exp;
        }

        /// <summary>Texto STEP: aspas duplicadas, barra invertida escapada, não-ASCII em \X2\.</summary>
        public static string S(string text)
        {
            if (text == null) return "$";
            var sb = new StringBuilder("'");
            foreach (var c in text)
            {
                if (c == '\'') sb.Append("''");
                else if (c == '\\') sb.Append("\\\\");
                else if (c >= 32 && c <= 126) sb.Append(c);
                else sb.Append("\\X2\\").Append(((int)c).ToString("X4")).Append("\\X0\\");
            }
            return sb.Append('\'').ToString();
        }

        /// <summary>GlobalId estável: mesmo desenho + mesmo objeto = mesmo GUID a cada exportação.</summary>
        string G(string key) => S(CompressGuid(MD5.HashData(Encoding.UTF8.GetBytes(_info.Seed + "|" + key))));

        const string Base64Chars = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz_$";

        /// <summary>Codifica 16 bytes no formato de 22 caracteres do IFC.</summary>
        public static string CompressGuid(byte[] bytes16)
        {
            var n = new BigInteger(bytes16, isUnsigned: true, isBigEndian: true);
            var chars = new char[22];
            for (int i = 21; i >= 0; i--)
            {
                chars[i] = Base64Chars[(int)(n & 63)];
                n >>= 6;
            }
            return new string(chars);
        }
    }
}
