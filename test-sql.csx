#r "nuget: Microsoft.Data.SqlClient, 6.0.1"
using Microsoft.Data.SqlClient;

var connStr = "Server=tcp:sql1003.site4now.net,1433;Database=db_ab2fbb_flow;User Id=db_ab2fbb_flow_admin;Password=u0hwjTvMfyFMVxn6x4YM;TrustServerCertificate=True;Encrypt=True;Connection Timeout=30;";
Console.WriteLine("Connecting...");
try {
    using var conn = new SqlConnection(connStr);
    conn.Open();
    Console.WriteLine("Connected OK!");
    conn.Close();
} catch (Exception ex) {
    Console.WriteLine($"Error: {ex.Message}");
    Console.WriteLine($"Inner: {ex.InnerException?.Message}");
}
