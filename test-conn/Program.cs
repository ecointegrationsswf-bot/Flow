using Microsoft.Data.SqlClient;

var connStr = "Server=tcp:sql1003.site4now.net,1433;Database=db_ab2fbb_flow;User Id=db_ab2fbb_flow_admin;Password=u0hwjTvMfyFMVxn6x4YM;TrustServerCertificate=True;Encrypt=True;Connection Timeout=30;";
Console.WriteLine("Connecting with Microsoft.Data.SqlClient 6.0.1...");
try {
    using var conn = new SqlConnection(connStr);
    conn.Open();
    Console.WriteLine("SUCCESS: Connected!");
    using var cmd = new SqlCommand("SELECT 1", conn);
    var result = cmd.ExecuteScalar();
    Console.WriteLine($"Query result: {result}");
} catch (Exception ex) {
    Console.WriteLine($"FAILED: {ex.Message}");
    if (ex.InnerException != null)
        Console.WriteLine($"Inner: {ex.InnerException.Message}");
}
