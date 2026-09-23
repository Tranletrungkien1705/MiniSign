# DEEPEN-LOG — MiniSign

- 2026-XX: Port nghiệp vụ ký số SHA1withRSA (hóa đơn điện tử) từ 2017.A.iNOS.InBrand (Dev20 idn.Skycic.InBrand: SignData.cs, SignConfig, RSAUtil, signTTHD/SignatureVerify). Thêm enum SignAlgorithm + field Algorithm vào Certificate/SignLog; SignService.SignAsync/VerifyAsync nhận thuật toán; endpoint /api/sign/sha1, /api/verify/sha1, /api/certinfo/{serial} (Program.cs + ApiV1Controller); 4 test mới. Build Release 0 error, 9/9 test pass.
