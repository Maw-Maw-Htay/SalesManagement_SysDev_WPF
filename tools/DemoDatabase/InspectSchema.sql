SET NOCOUNT ON;

SELECT N'# SQL Server database inventory — ' + QUOTENAME(DB_NAME());
SELECT N'';
SELECT CONCAT(N'Captured ', CONVERT(nvarchar(33), SYSDATETIMEOFFSET(), 127), N'. SQL Server ',
    CONVERT(nvarchar(128),SERVERPROPERTY('ProductVersion')), N', compatibility ', compatibility_level, N', collation `', collation_name, N'`.')
FROM sys.databases WHERE database_id = DB_ID();

SELECT N'';
SELECT N'## Tables and exact row counts';
SELECT N'| Table | Rows |';
SELECT N'| --- | ---: |';
DECLARE @counts nvarchar(max);
SELECT @counts = STRING_AGG(CONVERT(nvarchar(max), N'SELECT N''' + REPLACE(QUOTENAME(s.name) + N'.' + QUOTENAME(t.name), N'''', N'''''')
    + N''' AS [TableName], COUNT_BIG(*) AS [Rows] FROM ' + QUOTENAME(s.name) + N'.' + QUOTENAME(t.name)), N' UNION ALL ')
FROM sys.tables t JOIN sys.schemas s ON s.schema_id = t.schema_id WHERE t.is_ms_shipped = 0;
IF @counts IS NOT NULL
BEGIN
    SET @counts = N'SELECT CONCAT(N''| `'', [TableName], N''` | '', [Rows], N'' |'') FROM (' + @counts + N') AS counts ORDER BY [TableName]';
    EXEC sys.sp_executesql @counts;
END;

SELECT N'';
SELECT N'## Columns, identity, defaults, and computed expressions';
SELECT N'| Table | Column | SQL type | Nullable | Identity | Default or expression |';
SELECT N'| --- | --- | --- | --- | --- | --- |';
SELECT CONCAT(N'| `', QUOTENAME(s.name), N'.', QUOTENAME(t.name), N'` | `', c.name, N'` | `', ty.name,
    CASE WHEN ty.name IN ('varchar','char','varbinary','binary') THEN CONCAT('(', IIF(c.max_length=-1,'max',CONVERT(varchar(5),c.max_length)), ')')
         WHEN ty.name IN ('nvarchar','nchar') THEN CONCAT('(', IIF(c.max_length=-1,'max',CONVERT(varchar(5),c.max_length/2)), ')')
         WHEN ty.name IN ('numeric','decimal') THEN CONCAT('(', c.precision, ',', c.scale, ')')
         WHEN ty.name IN ('datetime2','datetimeoffset','time') THEN CONCAT('(', c.scale, ')') ELSE '' END,
    N'` | ', IIF(c.is_nullable=1,N'yes',N'no'), N' | ',
    CASE WHEN id.column_id IS NULL THEN N'—' ELSE CONCAT(CONVERT(nvarchar(32),id.seed_value), N' / ', CONVERT(nvarchar(32),id.increment_value)) END,
    N' | ', COALESCE(REPLACE(dc.definition,N'|',N'\|'),REPLACE(cc.definition,N'|',N'\|'),N'—'), N' |')
FROM sys.tables t JOIN sys.schemas s ON s.schema_id=t.schema_id
JOIN sys.columns c ON c.object_id=t.object_id JOIN sys.types ty ON ty.user_type_id=c.user_type_id
LEFT JOIN sys.identity_columns id ON id.object_id=c.object_id AND id.column_id=c.column_id
LEFT JOIN sys.default_constraints dc ON dc.object_id=c.default_object_id
LEFT JOIN sys.computed_columns cc ON cc.object_id=c.object_id AND cc.column_id=c.column_id
WHERE t.is_ms_shipped=0 ORDER BY s.name,t.name,c.column_id;

SELECT N'';
SELECT N'## Primary keys, unique constraints, and indexes';
SELECT N'| Table | Name | Type | PK | Unique | Key columns | Included columns | Filter |';
SELECT N'| --- | --- | --- | --- | --- | --- | --- | --- |';
SELECT CONCAT(N'| `', QUOTENAME(s.name) COLLATE DATABASE_DEFAULT,N'.',QUOTENAME(t.name) COLLATE DATABASE_DEFAULT,N'` | `',i.name COLLATE DATABASE_DEFAULT,N'` | ',i.type_desc COLLATE DATABASE_DEFAULT,
    N' | ',IIF(i.is_primary_key=1,'yes','no'), N' | ',IIF(i.is_unique=1,'yes','no'),N' | ', COALESCE(keys.cols COLLATE DATABASE_DEFAULT,N'—'),
    N' | ',COALESCE(included.cols COLLATE DATABASE_DEFAULT,N'—'),N' | ',COALESCE(REPLACE(i.filter_definition COLLATE DATABASE_DEFAULT,N'|',N'\|'),N'—'),N' |')
FROM sys.tables t JOIN sys.schemas s ON s.schema_id=t.schema_id JOIN sys.indexes i ON i.object_id=t.object_id AND i.index_id>0
OUTER APPLY (SELECT STRING_AGG(CONCAT(N'`',c.name,IIF(ic.is_descending_key=1,N' DESC',N' ASC'),N'`'),N', ')
    WITHIN GROUP(ORDER BY ic.key_ordinal) AS cols FROM sys.index_columns ic JOIN sys.columns c ON c.object_id=ic.object_id AND c.column_id=ic.column_id
    WHERE ic.object_id=i.object_id AND ic.index_id=i.index_id AND ic.key_ordinal>0) keys
OUTER APPLY (SELECT STRING_AGG(CONCAT(N'`',c.name,N'`'),N', ') WITHIN GROUP(ORDER BY ic.index_column_id) AS cols
    FROM sys.index_columns ic JOIN sys.columns c ON c.object_id=ic.object_id AND c.column_id=ic.column_id
    WHERE ic.object_id=i.object_id AND ic.index_id=i.index_id AND ic.is_included_column=1) included
WHERE t.is_ms_shipped=0 ORDER BY s.name,t.name,i.index_id;

SELECT N'';
SELECT N'## Foreign-key relationships';
SELECT N'| Constraint | Parent (dependent) | References | Column pairs | On delete | On update | Enabled / trusted |';
SELECT N'| --- | --- | --- | --- | --- | --- | --- |';
SELECT CONCAT(N'| `',f.name COLLATE DATABASE_DEFAULT,N'` | `',QUOTENAME(OBJECT_SCHEMA_NAME(f.parent_object_id)) COLLATE DATABASE_DEFAULT,N'.',QUOTENAME(OBJECT_NAME(f.parent_object_id)) COLLATE DATABASE_DEFAULT,
    N'` | `',QUOTENAME(OBJECT_SCHEMA_NAME(f.referenced_object_id)) COLLATE DATABASE_DEFAULT,N'.',QUOTENAME(OBJECT_NAME(f.referenced_object_id)) COLLATE DATABASE_DEFAULT,N'` | ',
    STRING_AGG(CONCAT(N'`',p.name COLLATE DATABASE_DEFAULT,N'` → `',r.name COLLATE DATABASE_DEFAULT,N'`'),N', ') WITHIN GROUP(ORDER BY fc.constraint_column_id),
    N' | ',f.delete_referential_action_desc COLLATE DATABASE_DEFAULT, N' | ',f.update_referential_action_desc COLLATE DATABASE_DEFAULT,N' | ',
    IIF(f.is_disabled=0,'yes','no'),N' / ',IIF(f.is_not_trusted=0,'yes','no'),N' |')
FROM sys.foreign_keys f JOIN sys.foreign_key_columns fc ON fc.constraint_object_id=f.object_id
JOIN sys.columns p ON p.object_id=fc.parent_object_id AND p.column_id=fc.parent_column_id
JOIN sys.columns r ON r.object_id=fc.referenced_object_id AND r.column_id=fc.referenced_column_id
GROUP BY f.name,f.parent_object_id,f.referenced_object_id,f.delete_referential_action_desc,f.update_referential_action_desc,f.is_disabled,f.is_not_trusted
ORDER BY OBJECT_NAME(f.parent_object_id),f.name;

SELECT N'';
SELECT N'## Check constraints and other application objects';
SELECT CONCAT(N'- `',QUOTENAME(OBJECT_SCHEMA_NAME(parent_object_id)),N'.',QUOTENAME(OBJECT_NAME(parent_object_id)),N'` `',name,N'` ',definition)
FROM sys.check_constraints ORDER BY parent_object_id,name;
SELECT CONCAT(N'- ',type_desc,N' ',COUNT(*)) FROM sys.objects WHERE is_ms_shipped=0 GROUP BY type_desc ORDER BY type_desc;

SELECT N'';
SELECT N'## Application account dependencies';
SELECT N'| Role | Master flag | Accounts | Active accounts | Accounts with a hash |';
SELECT N'| --- | ---: | ---: | ---: | ---: |';
SELECT CONCAT(N'| ',p.PoName,N' | ',p.PoFlag,N' | ',COUNT(e.EmID),N' | ',SUM(CASE WHEN e.EmFlag=0 THEN 1 ELSE 0 END),N' | ',
    SUM(CASE WHEN NULLIF(e.EmPasswordHash,N'') IS NOT NULL THEN 1 ELSE 0 END),N' |')
FROM dbo.M_Position p LEFT JOIN dbo.M_Employee e ON e.PoID=p.PoID GROUP BY p.PoName,p.PoFlag ORDER BY p.PoName;

SELECT N'';
SELECT N'## Recorded status and audit counts';
SELECT N'| Entity | Business flag | Management flag | Rows |';
SELECT N'| --- | --- | --- | ---: |';
SELECT CONCAT(N'| ',entity,N' | ',COALESCE(CONVERT(varchar(10),business),N'NULL / absent'),N' | ',management,N' | ',n,N' |')
FROM (SELECT N'受注' entity,OrStateFlag business,OrFlag management,COUNT_BIG(*) n FROM dbo.T_Order GROUP BY OrStateFlag,OrFlag
      UNION ALL SELECT N'注文',ChStateFlag,ChFlag,COUNT_BIG(*) FROM dbo.T_Chumon GROUP BY ChStateFlag,ChFlag
      UNION ALL SELECT N'発注',NULL,HaFlag,COUNT_BIG(*) FROM dbo.T_Hattyu GROUP BY HaFlag
      UNION ALL SELECT N'入庫',WaShelfFlag,WaFlag,COUNT_BIG(*) FROM dbo.T_Warehousing GROUP BY WaShelfFlag,WaFlag
      UNION ALL SELECT N'出庫',SyStateFlag,SyFlag,COUNT_BIG(*) FROM dbo.T_Syukko GROUP BY SyStateFlag,SyFlag
      UNION ALL SELECT N'入荷',ArStateFlag,ArFlag,COUNT_BIG(*) FROM dbo.T_Arrival GROUP BY ArStateFlag,ArFlag
      UNION ALL SELECT N'出荷',ShStateFlag,ShFlag,COUNT_BIG(*) FROM dbo.T_Shipment GROUP BY ShStateFlag,ShFlag
      UNION ALL SELECT N'売上',NULL,SaFlag,COUNT_BIG(*) FROM dbo.T_Sale GROUP BY SaFlag
      UNION ALL SELECT N'在庫',NULL,StFlag,COUNT_BIG(*) FROM dbo.T_Stock GROUP BY StFlag) a ORDER BY entity,business,management;
SELECT N'';
SELECT CONCAT(N'- 受注操作履歴 `',Operation,N'` — ',COUNT_BIG(*)) FROM dbo.T_OrderOperationHistory GROUP BY Operation ORDER BY Operation;
SELECT CONCAT(N'- 社員変更履歴 `',Operation,N'` — ',COUNT_BIG(*)) FROM dbo.T_EmployeeAssignmentHistory GROUP BY Operation ORDER BY Operation;
