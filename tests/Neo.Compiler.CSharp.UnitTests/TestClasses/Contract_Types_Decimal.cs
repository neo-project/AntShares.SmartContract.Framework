using System;

namespace Neo.Compiler.CSharp.UnitTests.TestClasses
{
    public class Contract_Types_Decimal : SmartContract.Framework.SmartContract
    {
        public static Decimal checkDecimal() { return 0.1M; }
    }
}
