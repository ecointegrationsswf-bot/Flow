-- Estado actual de asignaciones y acciones
SELECT Id, Name, Slug, AssignedPromptIds, AssignedActionIds
FROM Tenants
ORDER BY Name;

SELECT '---' AS separator;

SELECT Id, TenantId, Name, IsActive
FROM ActionDefinitions
ORDER BY Name;
