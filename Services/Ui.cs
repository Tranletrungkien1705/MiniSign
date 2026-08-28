using MiniSign.Models;
namespace MiniSign.Services;

public static class Ui
{
    public static (string text, string css) Cert(Certificate c)
    {
        if (c.Status == CertStatus.Revoked) return ("Đã thu hồi", "danger");
        if (DateTime.Today > c.NotAfter) return ("Hết hạn", "secondary");
        if (DateTime.Today < c.NotBefore) return ("Chưa hiệu lực", "warning");
        return ("Hiệu lực", "success");
    }
}
