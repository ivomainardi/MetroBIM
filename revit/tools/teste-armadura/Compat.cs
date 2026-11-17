// Só o que o ArmaduraIfc usa do Compat.cs do plugin (o original depende da API do Revit).
namespace MetroSP
{
    internal static class Compat
    {
        public static System.Numerics.BigInteger InteiroSemSinal(byte[] be) =>
            new System.Numerics.BigInteger(be, isUnsigned: true, isBigEndian: true);
    }
}
