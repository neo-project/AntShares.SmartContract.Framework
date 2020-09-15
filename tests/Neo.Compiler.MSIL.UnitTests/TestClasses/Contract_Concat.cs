using Neo.SmartContract.Framework;

namespace Neo.Compiler.MSIL.UnitTests.TestClasses
{
    public class Contract_Concat : SmartContract.Framework.SmartContract
    {
        public static string TestStringAdd1(string a)
        {
            return a + "hello";
        }

        public static string TestStringAdd2(string a, string b)
        {
            return a + b + "hello";
        }

        public static string TestStringAdd3(string a, string b, string c)
        {
            return a + b + c + "hello";
        }
    }
}