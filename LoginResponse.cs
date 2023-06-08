namespace MasterServer
{
    public class LoginResponse
    {
        public string username { get; set; } = "";
        public string redirect { get; set; } = "";
        public string token { get; set; } = "";
        public string status { get; set; } = "This User does not exist";
        public bool authorized { get; set; } = false;
    }
}