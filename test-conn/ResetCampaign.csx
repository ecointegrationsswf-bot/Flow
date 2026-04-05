#!/usr/bin/env dotnet-script
#r "nuget: Microsoft.Data.SqlClient, 6.0.1"

using Microsoft.Data.SqlClient;

var CONN_STR = "Server=tcp:sql1003.site4now.net,1433;Database=db_ab2fbb_flow;User Id=db_ab2fbb_flow_admin;Password=u0hwjTvMfyFMVxn6x4YM;TrustServerCertificate=True;Encrypt=True;Connection Timeout=30;MultipleActiveResultSets=True;";

await RunReset();

async Task RunReset() {
var conn = new SqlConnection(CONN_STR);
conn.Open();
Console.WriteLine("BD conectada OK");

// Obtener la campaña más reciente
var cmdGet = new SqlCommand("SELECT TOP 1 Id, Name, Status, TotalContacts, ProcessedContacts FROM Campaigns ORDER BY CreatedAt DESC", conn);
var r = cmdGet.ExecuteReader();
r.Read();
var campaignId   = r["Id"].ToString();
var campaignName = r["Name"].ToString();
Console.WriteLine($"Campaña: {campaignName} ({campaignId})");
Console.WriteLine($"  Estado actual: {r["Status"]}  Progreso: {r["ProcessedContacts"]}/{r["TotalContacts"]}");
r.Close();

// Resetear campaña a Running
var cmdCamp = new SqlCommand($"UPDATE Campaigns SET Status='Pending', ProcessedContacts=0, LaunchedAt=NULL, LaunchedByUserId=NULL WHERE Id='{campaignId}'", conn);
var rows = cmdCamp.ExecuteNonQuery();
Console.WriteLine($"  Campaña reseteada a Pending ({rows} fila)");

// Resetear todos los contactos a Pending
var cmdContacts = new SqlCommand($@"
    UPDATE CampaignContacts
    SET DispatchStatus='Pending', DispatchAttempts=0, DispatchError=NULL,
        ExternalMessageId=NULL, GeneratedMessage=NULL, SentAt=NULL, ClaimedAt=NULL
    WHERE CampaignId='{campaignId}'", conn);
var rowsC = cmdContacts.ExecuteNonQuery();
Console.WriteLine($"  Contactos reseteados a Pending ({rowsC} filas)");

// Verificar estado final
var cmdVerify = new SqlCommand($"SELECT Status, ProcessedContacts, TotalContacts FROM Campaigns WHERE Id='{campaignId}'", conn);
var r2 = cmdVerify.ExecuteReader();
r2.Read();
Console.WriteLine($"\n✓ Estado final: {r2["Status"]}  Progreso: {r2["ProcessedContacts"]}/{r2["TotalContacts"]}");
r2.Close();

Console.WriteLine("\nCampaña lista para re-lanzar desde el frontend.");
conn.Close();
}
