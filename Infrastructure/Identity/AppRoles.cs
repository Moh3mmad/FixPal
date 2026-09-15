namespace FixPal.Infrastructure.Identity
{
    public static class AppRoles
    {
        public const string Customer = "Customer";
        public const string Provider = "Provider";
        public const string Admin = "Admin";

        public static readonly string[] All =
        {
            Customer,
            Provider,
            Admin
        };
    }
}
