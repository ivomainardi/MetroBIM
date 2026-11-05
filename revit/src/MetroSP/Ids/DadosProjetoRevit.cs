using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.ExtensibleStorage;
using MetroSP.Ifc;

namespace MetroSP.Ids
{
    /// <summary>
    /// Onde os dados do IfcProject, IfcSite e IfcBuilding ficam no Revit. Sempre que existe, é o campo que o
    /// exportador IFC da Autodesk lê; o que não existe no modelo é criado como parâmetro compartilhado de texto
    /// (grupo IFC) nas Informações do projeto. O Pset MetroSP_Projeto fica guardado no modelo (Extensible Storage).
    /// </summary>
    public static class DadosProjetoRevit
    {
        /// <summary>Parâmetro, GUID (se for criado pela MetroSP) e descrição.</summary>
        private class Campo
        {
            public string Nome;
            public Guid Guid;
            public string Descricao;
        }

        private static readonly Campo DescricaoProjeto = new Campo { Nome = "IfcDescription", Guid = new Guid("7d1e4a2b-3c5f-4e8a-9b60-1f2e3d4c5b01"), Descricao = "Descrição do IfcProject (lida pelo exportador IFC)." };
        private static readonly Campo FaseProjeto = new Campo { Nome = "Project Phase", Guid = new Guid("7d1e4a2b-3c5f-4e8a-9b60-1f2e3d4c5b02"), Descricao = "Fase do IfcProject (lida pelo exportador IFC)." };
        private static readonly Campo NomeSite = new Campo { Nome = LocalizacaoIfc.ParametroSite, Guid = new Guid("5b3c1f4e-8d2a-4c7e-9f61-0a2d6e4b7c19"), Descricao = "Nome do IfcSite na exportação IFC (lido pelo exportador da Autodesk)." };
        private static readonly Campo LongoSite = new Campo { Nome = "SiteLongName", Guid = new Guid("7d1e4a2b-3c5f-4e8a-9b60-1f2e3d4c5b03"), Descricao = "Nome longo do IfcSite (lido pelo exportador IFC)." };
        private static readonly Campo DescricaoSite = new Campo { Nome = "SiteDescription", Guid = new Guid("7d1e4a2b-3c5f-4e8a-9b60-1f2e3d4c5b04"), Descricao = "Descrição do IfcSite (lida pelo exportador IFC)." };
        private static readonly Campo LongoEdificio = new Campo { Nome = "BuildingLongName", Guid = new Guid("7d1e4a2b-3c5f-4e8a-9b60-1f2e3d4c5b05"), Descricao = "Nome longo do IfcBuilding (lido pelo exportador IFC)." };
        private static readonly Campo DescricaoEdificio = new Campo { Nome = "BuildingDescription", Guid = new Guid("7d1e4a2b-3c5f-4e8a-9b60-1f2e3d4c5b06"), Descricao = "Descrição do IfcBuilding (lida pelo exportador IFC)." };

        /// <summary>Onde cada campo da janela fica no Revit (vai na dica).</summary>
        public static readonly Dictionary<string, string> Dicas = new Dictionary<string, string>
        {
            ["Projeto.Nome"] = "No Revit: Informações do projeto › Número do projeto.",
            ["Projeto.NomeLongo"] = "No Revit: Informações do projeto › Nome do projeto.",
            ["Projeto.Descricao"] = "No Revit: Informações do projeto › IfcDescription (criado se não existir).",
            ["Projeto.Fase"] = "No Revit: Informações do projeto › Project Phase (criado se não existir).",
            ["Terreno.Nome"] = "No Revit: Informações do projeto › SiteName (criado se não existir).",
            ["Terreno.NomeLongo"] = "No Revit: Informações do projeto › SiteLongName (criado se não existir).",
            ["Terreno.Descricao"] = "No Revit: Informações do projeto › SiteDescription (criado se não existir).",
            ["Terreno.Endereco"] = "No Revit: Manage › Location › endereço do projeto.",
            ["Edificio.Nome"] = "No Revit: Informações do projeto › Nome do edifício.",
            ["Edificio.NomeLongo"] = "No Revit: Informações do projeto › BuildingLongName (criado se não existir).",
            ["Edificio.Descricao"] = "No Revit: Informações do projeto › BuildingDescription (criado se não existir).",
            ["Edificio.Endereco"] = "No Revit: Informações do projeto › Endereço.",
        };

        public static DadosProjetoIfc Ler(Document doc)
        {
            ProjectInfo info = doc.ProjectInformation;
            string edificio;
            try { edificio = info?.BuildingName ?? ""; }
            catch (Autodesk.Revit.Exceptions.InvalidOperationException) { edificio = ""; }
            SiteLocation site = doc.ActiveProjectLocation?.GetSiteLocation();
            return new DadosProjetoIfc
            {
                Projeto = new DadosEntidadeIfc
                {
                    Nome = info?.Number ?? "",
                    NomeLongo = info?.Name ?? "",
                    Descricao = Texto(info, DescricaoProjeto),
                    Fase = Texto(info, FaseProjeto),
                },
                Terreno = new DadosEntidadeIfc
                {
                    Nome = Texto(info, NomeSite),
                    NomeLongo = Texto(info, LongoSite),
                    Descricao = Texto(info, DescricaoSite),
                    Endereco = site?.PlaceName ?? "",
                },
                Edificio = new DadosEntidadeIfc
                {
                    Nome = edificio,
                    NomeLongo = Texto(info, LongoEdificio),
                    Descricao = Texto(info, DescricaoEdificio),
                    Endereco = info?.Address ?? "",
                },
                Propriedades = LerPropriedades(info),
            };
        }

        /// <summary>
        /// Grava no Revit o que difere entre <paramref name="antes"/> e <paramref name="novo"/> e anota em
        /// <paramref name="mudou"/>. Precisa de transação aberta.
        /// </summary>
        public static void Gravar(Document doc, DadosProjetoIfc antes, DadosProjetoIfc novo, List<string> mudou)
        {
            ProjectInfo info = doc.ProjectInformation;
            bool Dif(string a, string b) => !string.Equals((a ?? "").Trim(), (b ?? "").Trim(), StringComparison.Ordinal);

            // Cria de uma vez os parâmetros que faltam e vão receber valor.
            var gravar = new List<(Campo campo, string valor, string rotulo)>
            {
                (DescricaoProjeto, novo.Projeto.Descricao, "descrição do IfcProject"),
                (FaseProjeto, novo.Projeto.Fase, "fase do IfcProject"),
                (NomeSite, novo.Terreno.Nome, "nome do IfcSite"),
                (LongoSite, novo.Terreno.NomeLongo, "nome longo do IfcSite"),
                (DescricaoSite, novo.Terreno.Descricao, "descrição do IfcSite"),
                (LongoEdificio, novo.Edificio.NomeLongo, "nome longo do IfcBuilding"),
                (DescricaoEdificio, novo.Edificio.Descricao, "descrição do IfcBuilding"),
            }.Where(g => Dif(Texto(info, g.campo), g.valor)).ToList();
            CriarParametros(doc, gravar.Where(g => info.LookupParameter(g.campo.Nome) == null).Select(g => g.campo).ToList());
            foreach (var g in gravar)
            {
                Parameter p = info.LookupParameter(g.campo.Nome);
                if (p == null || p.IsReadOnly || p.StorageType != StorageType.String || !p.Set((g.valor ?? "").Trim()))
                    throw new InvalidOperationException($"não foi possível gravar a {g.rotulo} no parâmetro {g.campo.Nome} das Informações do projeto" +
                        (p != null && p.StorageType != StorageType.String ? " (ele existe no modelo, mas não é de texto)." : "."));
                mudou.Add($"Informações do projeto: {g.campo.Nome} ({g.rotulo}) \"{(g.valor ?? "").Trim()}\"");
            }

            if (Dif(antes.Projeto.Nome, novo.Projeto.Nome))
            {
                info.Number = (novo.Projeto.Nome ?? "").Trim();
                mudou.Add($"Informações do projeto: número do projeto (nome do IfcProject) \"{info.Number}\"");
            }
            if (Dif(antes.Projeto.NomeLongo, novo.Projeto.NomeLongo))
            {
                info.Name = (novo.Projeto.NomeLongo ?? "").Trim();
                mudou.Add($"Informações do projeto: nome do projeto (nome longo do IfcProject) \"{info.Name}\"");
            }
            if (Dif(antes.Edificio.Nome, novo.Edificio.Nome))
            {
                info.BuildingName = (novo.Edificio.Nome ?? "").Trim();
                mudou.Add($"Informações do projeto: nome do edifício \"{info.BuildingName}\"");
            }
            if (Dif(antes.Edificio.Endereco, novo.Edificio.Endereco))
            {
                info.Address = (novo.Edificio.Endereco ?? "").Trim();
                mudou.Add($"Informações do projeto: endereço \"{info.Address}\"");
            }
            if (Dif(antes.Terreno.Endereco, novo.Terreno.Endereco))
            {
                SiteLocation site = doc.ActiveProjectLocation?.GetSiteLocation()
                    ?? throw new InvalidOperationException("o modelo não tem Location (Manage › Location) para o endereço do IfcSite.");
                site.PlaceName = (novo.Terreno.Endereco ?? "").Trim();
                mudou.Add($"Location: endereço do projeto \"{site.PlaceName}\"");
            }
            if (!MesmasPropriedades(antes.Propriedades, novo.Propriedades))
            {
                GravarPropriedades(info, novo.Propriedades);
                int n = Validas(novo.Propriedades).Count();
                mudou.Add($"Modelo: Pset {DadosProjetoIfc.NomePset} com {n} propriedade(s)");
            }
        }

        /// <summary>Quantos campos diferem (para o resumo da janela).</summary>
        public static int Diferencas(DadosProjetoIfc a, DadosProjetoIfc b)
        {
            int n = 0;
            void C(string x, string y) { if (!string.Equals((x ?? "").Trim(), (y ?? "").Trim(), StringComparison.Ordinal)) n++; }
            C(a.Projeto.Nome, b.Projeto.Nome); C(a.Projeto.NomeLongo, b.Projeto.NomeLongo); C(a.Projeto.Descricao, b.Projeto.Descricao); C(a.Projeto.Fase, b.Projeto.Fase);
            C(a.Terreno.Nome, b.Terreno.Nome); C(a.Terreno.NomeLongo, b.Terreno.NomeLongo); C(a.Terreno.Descricao, b.Terreno.Descricao); C(a.Terreno.Endereco, b.Terreno.Endereco);
            C(a.Edificio.Nome, b.Edificio.Nome); C(a.Edificio.NomeLongo, b.Edificio.NomeLongo); C(a.Edificio.Descricao, b.Edificio.Descricao); C(a.Edificio.Endereco, b.Edificio.Endereco);
            if (!MesmasPropriedades(a.Propriedades, b.Propriedades)) n++;
            return n;
        }

        // ---------- parâmetros ----------

        private static string Texto(ProjectInfo info, Campo c)
        {
            Parameter p = info?.LookupParameter(c.Nome);
            if (p == null) return "";
            return (p.StorageType == StorageType.String ? p.AsString() : p.AsValueString()) ?? "";
        }

        /// <summary>Cria parâmetros compartilhados de texto (grupo IFC) nas Informações do projeto, por um arquivo temporário.</summary>
        private static void CriarParametros(Document doc, List<Campo> campos)
        {
            if (campos.Count == 0) return;
            Autodesk.Revit.ApplicationServices.Application app = doc.Application;
            string original = app.SharedParametersFilename;
            string temp = Path.Combine(Path.GetTempPath(), "MetroSP_DadosProjeto_" + Guid.NewGuid().ToString("N") + ".txt");
            try
            {
                File.WriteAllText(temp, string.Empty);
                app.SharedParametersFilename = temp;
                DefinitionGroup grupo = app.OpenSharedParameterFile().Groups.Create("MetroSP");
                CategorySet cats = app.Create.NewCategorySet();
                cats.Insert(Category.GetCategory(doc, BuiltInCategory.OST_ProjectInformation));
                foreach (Campo c in campos)
                {
                    var opcoes = new ExternalDefinitionCreationOptions(c.Nome, SpecTypeId.String.Text) { GUID = c.Guid, Description = c.Descricao };
                    Definition def = grupo.Definitions.Create(opcoes);
                    doc.ParameterBindings.Insert(def, app.Create.NewInstanceBinding(cats), GroupTypeId.Ifc);
                }
            }
            finally
            {
                try { app.SharedParametersFilename = original ?? ""; } catch { }
                try { File.Delete(temp); } catch { }
            }
        }

        // ---------- Pset MetroSP_Projeto no modelo (Extensible Storage nas Informações do projeto) ----------

        private static readonly Guid GuidSchema = new Guid("3f6b2d1a-8e4c-4b7a-a5d2-9c1e0f7b6a31");
        private const string CampoJson = "Propriedades";

        private static Schema Esquema()
        {
            Schema s = Schema.Lookup(GuidSchema);
            if (s != null) return s;
            var b = new SchemaBuilder(GuidSchema);
            b.SetSchemaName("MetroSP_DadosProjeto");
            b.SetVendorId("METROSP");
            b.SetReadAccessLevel(AccessLevel.Public);
            b.SetWriteAccessLevel(AccessLevel.Public);
            b.SetDocumentation("Propriedades do Pset MetroSP_Projeto (JSON), gravadas no IfcProject na exportação MetroSP.");
            b.AddSimpleField(CampoJson, typeof(string));
            return b.Finish();
        }

        private static Dictionary<string, string> LerPropriedades(ProjectInfo info)
        {
            try
            {
                Schema s = Schema.Lookup(GuidSchema);
                if (info == null || s == null) return new Dictionary<string, string>();
                Entity e = info.GetEntity(s);
                if (e == null || !e.IsValid()) return new Dictionary<string, string>();
                string json = e.Get<string>(CampoJson);
                return DadosProjetoIfc.DeJson("{\"Propriedades\":" + (string.IsNullOrWhiteSpace(json) ? "{}" : json) + "}")?.Propriedades
                    ?? new Dictionary<string, string>();
            }
            catch { return new Dictionary<string, string>(); }
        }

        private static void GravarPropriedades(ProjectInfo info, Dictionary<string, string> props)
        {
            var e = new Entity(Esquema());
            string json = System.Text.Json.JsonSerializer.Serialize(Validas(props).ToDictionary(kv => kv.Key.Trim(), kv => kv.Value.Trim()),
                new System.Text.Json.JsonSerializerOptions { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
            e.Set(CampoJson, json);
            info.SetEntity(e);
        }

        private static IEnumerable<KeyValuePair<string, string>> Validas(Dictionary<string, string> p) =>
            (p ?? new Dictionary<string, string>()).Where(kv => !string.IsNullOrWhiteSpace(kv.Key) && !string.IsNullOrWhiteSpace(kv.Value));

        private static bool MesmasPropriedades(Dictionary<string, string> a, Dictionary<string, string> b)
        {
            var x = Validas(a).ToDictionary(kv => kv.Key.Trim(), kv => kv.Value.Trim());
            var y = Validas(b).ToDictionary(kv => kv.Key.Trim(), kv => kv.Value.Trim());
            return x.Count == y.Count && x.All(kv => y.TryGetValue(kv.Key, out string v) && v == kv.Value);
        }
    }
}
