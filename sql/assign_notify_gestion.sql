UPDATE Tenants
SET AssignedActionIds = '["1df8854f-4932-45f9-bf74-2b81377f8969","21b6a09a-36ae-4194-b861-bdfd86c061e9","aa000001-1111-4000-8000-000000000001","aa000002-2222-4000-8000-000000000002","d221c23d-fa0c-41ad-b356-60df013c877f","ddbde0ec-f534-477c-85a3-0282102f97ca"]'
WHERE Name = 'Prueba';

SELECT Name, AssignedActionIds FROM Tenants WHERE Name = 'Prueba';
