using System.ComponentModel.DataAnnotations;
namespace FixPal.Models.Enums;
public enum RequestType
{
    [Display(Name = "خدمة صيانة خاصة")] PrivateService = 1,
    [Display(Name = "بلاغ عن مكان عام")] PublicReport = 2
}
