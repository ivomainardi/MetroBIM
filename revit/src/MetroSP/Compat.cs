// Diferenças entre as versões do Revit (2022 a 2027) e do .NET (Framework 4.8 e .NET 8/10), num lugar só,
// para o resto do código escrever do jeito novo. As constantes vêm do MetroSP.csproj:
//   REVIT_ANTES_2023: Revit 2022          REVIT_ANTES_2024: Revit 2022 e 2023
//   REVIT_ANTES_2025 / NETFRAMEWORK: Revit 2022 a 2024 (.NET Framework 4.8)
using System.Security.Cryptography;
using System.Text;
using Autodesk.Revit.DB;

namespace MetroSP
{
    internal static class Compat
    {
        /// <summary>ElementId a partir do número (no Revit 2022/2023 o Id ainda é int).</summary>
        public static ElementId NovoId(long valor) =>
#if REVIT_ANTES_2024
            new ElementId((int)valor);
#else
            new ElementId(valor);
#endif

        /// <summary>
        /// Parâmetro "Exportar para IFC como" da instância ou do tipo. No Revit 2022 ainda não é parâmetro interno:
        /// é o parâmetro compartilhado IfcExportAs (no tipo também IfcExportAs[Type]).
        /// </summary>
        public static Parameter ExportarComo(Element e, bool doTipo)
        {
            if (e == null) return null;
#if REVIT_ANTES_2023
            return (doTipo ? e.LookupParameter("IfcExportAs[Type]") : null) ?? e.LookupParameter("IfcExportAs");
#else
            return e.get_Parameter(doTipo ? BuiltInParameter.IFC_EXPORT_ELEMENT_TYPE_AS : BuiltInParameter.IFC_EXPORT_ELEMENT_AS);
#endif
        }

        /// <summary>Inteiro sem sinal a partir de bytes em ordem big-endian (o construtor com isUnsigned só existe no .NET 8+).</summary>
        public static System.Numerics.BigInteger InteiroSemSinal(byte[] bigEndian)
        {
#if NETFRAMEWORK
            var le = new byte[bigEndian.Length + 1];   // último byte 0 = positivo
            for (int i = 0; i < bigEndian.Length; i++) le[i] = bigEndian[bigEndian.Length - 1 - i];
            return new System.Numerics.BigInteger(le);
#else
            return new System.Numerics.BigInteger(bigEndian, isUnsigned: true, isBigEndian: true);
#endif
        }

#if REVIT_ANTES_2024
        extension(ElementId id)
        {
            /// <summary>Número do Id (no Revit 2024+ é a propriedade ElementId.Value).</summary>
            public long Value => id.IntegerValue;
        }
#endif

#if REVIT_ANTES_2023
        extension(Category c)
        {
            /// <summary>Categoria interna (no Revit 2023+ é a propriedade Category.BuiltInCategory).</summary>
            public BuiltInCategory BuiltInCategory => (BuiltInCategory)c.Id.IntegerValue;
        }
#endif
    }

#if NETFRAMEWORK
    // .NET Framework 4.8 (Revit 2024 e anteriores): o que o .NET 8 já traz. Uma classe por tipo estendido.
    internal static class CompatMd5
    {
        extension(MD5)
        {
            public static byte[] HashData(byte[] dados) { using (MD5 h = MD5.Create()) return h.ComputeHash(dados); }
        }
    }

    internal static class CompatSha256
    {
        extension(SHA256)
        {
            public static byte[] HashData(byte[] dados) { using (SHA256 h = SHA256.Create()) return h.ComputeHash(dados); }
        }
    }

    internal static class CompatAleatorio
    {
        extension(RandomNumberGenerator)
        {
            public static byte[] GetBytes(int quantos)
            {
                var b = new byte[quantos];
                using (RandomNumberGenerator r = RandomNumberGenerator.Create()) r.GetBytes(b);
                return b;
            }
        }
    }

    internal static class CompatEncoding
    {
        extension(Encoding)
        {
            public static Encoding Latin1 => Encoding.GetEncoding(28591);
        }
    }
#endif
}
