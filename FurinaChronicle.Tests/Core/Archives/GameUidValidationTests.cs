using FurinaChronicle.Core.Archives;

namespace FurinaChronicle.Tests.Core.Archives
{
    public sealed class GameUidValidationTests
    {
        [Theory]
        [InlineData("100000000")]
        [InlineData("108060050")]
        [InlineData("123980674")]
        [InlineData("180000056")]
        [InlineData("199999999")]
        [InlineData("200000000")]
        [InlineData("216316388")]
        [InlineData("274079782")]
        [InlineData("299999999")]
        [InlineData("300000000")]
        [InlineData("320786201")]
        [InlineData("399999999")]
        [InlineData("500000000")]
        [InlineData("599999999")]
        [InlineData("601078294")]
        [InlineData("600000000")]
        [InlineData("699999999")]
        [InlineData("700000000")]
        [InlineData("799999999")]
        [InlineData("800000000")]
        [InlineData("886320896")]
        [InlineData("899999999")]
        [InlineData("1800000000")]
        [InlineData("1826223017")]
        [InlineData("1899999999")]
        [InlineData("900000000")]
        [InlineData("957865424")]
        [InlineData("999999999")]
        public void Validate_ValidUid_ReturnsTrue(string? uid)
        {
            Assert.True(GameUidValidation.IsValidUid(uid));
        }

        [Theory]
        [InlineData("10000000")]
        [InlineData("1000000000")]
        [InlineData("000000001")]
        [InlineData("10000000a")]
        [InlineData("a00000000")]
        [InlineData("abcdefghi")]
        [InlineData("1234567890")]
        [InlineData("412386749")]
        [InlineData("18000000001")]
        [InlineData("1999999999")]
        [InlineData("")]
        [InlineData("not a uid")]
        [InlineData("""4123""213'@d;[]/-*-#""")]
        [InlineData(" ")]
        [InlineData(" 123980674 ")]
        [InlineData("\t 123980674 \t")]
        [InlineData(" 123980674")]
        [InlineData("123980674 ")]
        [InlineData("１２３９８０６７４")]
        [InlineData("١٢٣٩٨٠٦٧٤")]
        [InlineData(null)]
        public void Validate_InvalidUid_ReturnsFalse(string? uid)
        {
            Assert.False(GameUidValidation.IsValidUid(uid));
        }
    }
}
