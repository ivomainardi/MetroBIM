using System;
using System.Linq;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace MetroSP.Ifc
{
    /// <summary>
    /// Marca no IFC exportado que ele passou pelo plugin MetroSP e com qual versão, nos dois lugares que as normas preveem
    /// (este arquivo é igual no Revit e no Civil 3D):
    /// <list type="bullet">
    /// <item>cabeçalho (ISO 10303-21): "MetroSP [software] [versão]" no fim do preprocessor_version do FILE_NAME, que é o
    /// programa que gravou o arquivo. O texto da Autodesk fica, e o originating_system (de onde vieram os dados) não muda;</item>
    /// <item>modelo (ISO 16739): IfcApplication do MetroSP como LastModifyingApplication no IfcOwnerHistory do IfcProject,
    /// com ChangeAction MODIFIED e a data. A aplicação de origem (Revit, Civil 3D) continua como OwningApplication.</item>
    /// </list>
    /// Exportar de novo o mesmo arquivo substitui a marca anterior, sem repetir.
    /// </summary>
    public static class OrigemIfc
    {
        /// <summary>Quem desenvolve o plugin (IfcOrganization da IfcApplication), como no selo da Ribbon.</summary>
        public const string Desenvolvedor = "Ação BIM · Metrô";
        private const string Separador = " | ";

        /// <summary>Versão do plugin (Major.Minor.Build da DLL, que vem do &lt;Version&gt; do .csproj).</summary>
        public static string Versao
        {
            get
            {
                var v = typeof(OrigemIfc).Assembly.GetName().Version;
                return v == null ? "?" : $"{v.Major}.{v.Minor}.{Math.Max(v.Build, 0)}";
            }
        }

        /// <summary>Texto da marca, por exemplo "MetroSP Revit 0.3.1".</summary>
        public static string Texto(string plugin, string versao = null) => $"{plugin} {versao ?? Versao}";

        /// <summary>
        /// Grava a marca no cabeçalho e no IfcOwnerHistory do IfcProject. <paramref name="plugin"/>: "MetroSP Revit" ou
        /// "MetroSP Civil 3D"; o identificador da aplicação sai dele sem espaços ("MetroSP.Revit", "MetroSP.Civil3D").
        /// </summary>
        public static void Marcar(StepModel m, string plugin, Action<string> log = null)
        {
            string versao = Versao;
            MarcarCabecalho(m, Texto(plugin, versao));
            bool app = MarcarAplicacao(m, plugin, versao);
            log?.Invoke($"Origem: {Texto(plugin, versao)} no cabeçalho (preprocessor_version)" +
                        (app ? " e como IfcApplication no IfcProject." : "; IFC sem IfcProject, sem IfcApplication."));
        }

        // ------------------------------------------------------------------ cabeçalho

        /// <summary>Acrescenta a marca ao preprocessor_version (5º argumento do FILE_NAME). Tira uma marca MetroSP anterior.</summary>
        public static void MarcarCabecalho(StepModel m, string marca)
        {
            string h = m.Header ?? "";
            int ini = h.IndexOf("FILE_NAME", StringComparison.OrdinalIgnoreCase);
            if (ini < 0) return;
            int abre = h.IndexOf('(', ini);
            if (abre < 0) return;
            int fecha = FechaParentese(h, abre);
            if (fecha < 0) return;
            // Alguns exportadores comentam cada campo (/* name */ ...): os comentários saem.
            string dentro = Regex.Replace(h.Substring(abre + 1, fecha - abre - 1), @"/\*.*?\*/", "", RegexOptions.Singleline);
            var args = StepModel.SplitArgs(dentro);
            if (args.Count < 7) return;
            string atual = StepModel.Text(args[4]) ?? "";
            int ant = atual.IndexOf(Separador + "MetroSP ", StringComparison.Ordinal);
            if (ant >= 0) atual = atual.Substring(0, ant);
            else if (atual.StartsWith("MetroSP", StringComparison.Ordinal)) atual = "";
            args[4] = S(atual.Trim().Length == 0 ? marca : atual + Separador + marca);
            m.Header = h.Substring(0, abre + 1) + string.Join(",", args) + h.Substring(fecha);
        }

        private static int FechaParentese(string s, int abre)
        {
            int depth = 0;
            bool str = false;
            for (int i = abre; i < s.Length; i++)
            {
                char c = s[i];
                if (c == '\'') { str = !str; continue; }
                if (str) continue;
                if (c == '(') depth++;
                else if (c == ')' && --depth == 0) return i;
            }
            return -1;
        }

        // ------------------------------------------------------------------ IfcApplication

        /// <summary>
        /// IfcOwnerHistory próprio do IfcProject, copiado do que o exportador gravou (o mesmo usuário e a mesma aplicação de
        /// origem), com LastModifyingApplication = MetroSP, ChangeAction MODIFIED e LastModifiedDate = agora. Os outros
        /// objetos continuam com o histórico do exportador. Sem histórico (IFC4 permite), cria um com o usuário do Windows.
        /// </summary>
        public static bool MarcarAplicacao(StepModel m, string plugin, string versao)
        {
            var projeto = m.OfType("IFCPROJECT").FirstOrDefault();
            if (projeto == null || projeto.Args.Count < 2) return false;
            string identificador = plugin.Replace(" ", "");
            string agora = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture);

            var hist = m.Get(StepModel.Ref(projeto.Args[1]));
            if (hist != null && hist.Type != "IFCOWNERHISTORY") hist = null;

            // Exportação repetida: o histórico do IfcProject já é o do MetroSP; só atualiza a versão e a data.
            var appAnterior = hist != null && hist.Args.Count >= 8 ? m.Get(StepModel.Ref(hist.Args[6])) : null;
            if (appAnterior != null && appAnterior.Type == "IFCAPPLICATION" && appAnterior.Args.Count >= 4 &&
                (StepModel.Text(appAnterior.Args[3]) ?? "").StartsWith("MetroSP", StringComparison.Ordinal))
            {
                m.SetArg(appAnterior.Id, 1, S(versao));
                m.SetArg(appAnterior.Id, 2, S(plugin));
                m.SetArg(appAnterior.Id, 3, S(identificador));
                m.SetArg(hist.Id, 4, agora);
                return true;
            }

            int org = m.Add("IFCORGANIZATION", "$", S(Desenvolvedor), "$", "$", "$");
            int app = m.Add("IFCAPPLICATION", "#" + org, S(versao), S(plugin), S(identificador));

            string usuario, aplicacaoOrigem, estado, criacao;
            if (hist != null && hist.Args.Count >= 8)
            {
                usuario = hist.Args[0];
                aplicacaoOrigem = hist.Args[1];
                estado = hist.Args[2];
                criacao = hist.Args[7];
            }
            else
            {
                string nome = Environment.UserName;
                int pessoa = m.Add("IFCPERSON", "$", S(string.IsNullOrWhiteSpace(nome) ? "?" : nome), "$", "$", "$", "$", "$", "$");
                usuario = "#" + m.Add("IFCPERSONANDORGANIZATION", "#" + pessoa, "#" + org, "$");
                aplicacaoOrigem = "#" + app;
                estado = "$";
                criacao = agora;
            }
            int novo = m.Add("IFCOWNERHISTORY", usuario, aplicacaoOrigem, estado, ".MODIFIED.", agora, usuario, "#" + app, criacao);
            m.SetArg(projeto.Id, 1, "#" + novo);
            return true;
        }

        // ------------------------------------------------------------------ texto STEP

        /// <summary>Texto STEP: aspas duplicadas, barra invertida escapada, não-ASCII em \X2\.</summary>
        private static string S(string text)
        {
            if (text == null) return "$";
            var sb = new StringBuilder("'");
            foreach (char c in text)
            {
                if (c == '\'') sb.Append("''");
                else if (c == '\\') sb.Append("\\\\");
                else if (c >= 32 && c <= 126) sb.Append(c);
                else sb.Append("\\X2\\").Append(((int)c).ToString("X4")).Append("\\X0\\");
            }
            return sb.Append('\'').ToString();
        }
    }
}
