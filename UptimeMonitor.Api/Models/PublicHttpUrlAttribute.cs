using System.ComponentModel.DataAnnotations;
using UptimeMonitor.Api.Services;

namespace UptimeMonitor.Api.Models;

public sealed class PublicHttpUrlAttribute : ValidationAttribute
{
    public PublicHttpUrlAttribute() : base("Укажите публичный HTTP/HTTPS URL без логина, пароля и фрагмента, на стандартном порту 80/443.") { }
    public override bool IsValid(object? value) => value is string url && PublicUrlPolicy.TryValidate(url, out _);
}
