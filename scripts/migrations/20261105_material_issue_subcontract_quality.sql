-- Additive schema for existing SMES MySQL 8.4 deployments.
-- Generated from the isolated _e2e EF EnsureCreated schema. Backup and preflight first.
-- Never run against an unrelated schema; caller supplies the selected database.
-- No DROP, DELETE, TRUNCATE, or table rebuild statements.
CREATE TABLE IF NOT EXISTS `MaterialIssueJob` (
  `Id` bigint NOT NULL AUTO_INCREMENT,
  `BillId` bigint NOT NULL,
  `WorkOrderId` bigint NOT NULL,
  `IdempotencyKey` varchar(128) CHARACTER SET utf8mb4 COLLATE utf8mb4_0900_ai_ci NOT NULL,
  `PayloadHash` varchar(64) CHARACTER SET utf8mb4 COLLATE utf8mb4_0900_ai_ci NOT NULL,
  `JobStatus` int NOT NULL,
  `Attempts` int NOT NULL,
  `Error` varchar(500) CHARACTER SET utf8mb4 COLLATE utf8mb4_0900_ai_ci DEFAULT NULL,
  `CompletedAt` datetime(6) DEFAULT NULL,
  `TenantId` bigint DEFAULT NULL,
  `Code` varchar(500) CHARACTER SET utf8mb4 COLLATE utf8mb4_0900_ai_ci DEFAULT NULL,
  `Name` varchar(500) CHARACTER SET utf8mb4 COLLATE utf8mb4_0900_ai_ci DEFAULT NULL,
  `Status` int NOT NULL,
  `Remark` varchar(500) CHARACTER SET utf8mb4 COLLATE utf8mb4_0900_ai_ci DEFAULT NULL,
  `CreatedAt` datetime(6) NOT NULL,
  `CreatedBy` bigint DEFAULT NULL,
  `UpdatedAt` datetime(6) DEFAULT NULL,
  `UpdatedBy` bigint DEFAULT NULL,
  `IsDeleted` tinyint(1) NOT NULL,
  PRIMARY KEY (`Id`),
  UNIQUE KEY `IX_MaterialIssueJob_TenantId_BillId` (`TenantId`,`BillId`),
  UNIQUE KEY `IX_MaterialIssueJob_TenantId_IdempotencyKey` (`TenantId`,`IdempotencyKey`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

CREATE TABLE IF NOT EXISTS `MaterialIssueLineLink` (
  `Id` bigint NOT NULL AUTO_INCREMENT,
  `BillLineId` bigint NOT NULL,
  `WorkOrderMaterialId` bigint NOT NULL,
  `Qty` decimal(18,4) NOT NULL,
  `TenantId` bigint DEFAULT NULL,
  `Code` varchar(500) CHARACTER SET utf8mb4 COLLATE utf8mb4_0900_ai_ci DEFAULT NULL,
  `Name` varchar(500) CHARACTER SET utf8mb4 COLLATE utf8mb4_0900_ai_ci DEFAULT NULL,
  `Status` int NOT NULL,
  `Remark` varchar(500) CHARACTER SET utf8mb4 COLLATE utf8mb4_0900_ai_ci DEFAULT NULL,
  `CreatedAt` datetime(6) NOT NULL,
  `CreatedBy` bigint DEFAULT NULL,
  `UpdatedAt` datetime(6) DEFAULT NULL,
  `UpdatedBy` bigint DEFAULT NULL,
  `IsDeleted` tinyint(1) NOT NULL,
  PRIMARY KEY (`Id`),
  UNIQUE KEY `IX_MaterialIssueLineLink_TenantId_BillLineId` (`TenantId`,`BillLineId`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

CREATE TABLE IF NOT EXISTS `QualityJudgeJob` (
  `Id` bigint NOT NULL AUTO_INCREMENT,
  `InspectionId` bigint NOT NULL,
  `IdempotencyKey` varchar(128) CHARACTER SET utf8mb4 COLLATE utf8mb4_0900_ai_ci NOT NULL,
  `PayloadHash` varchar(64) CHARACTER SET utf8mb4 COLLATE utf8mb4_0900_ai_ci NOT NULL,
  `Result` int NOT NULL,
  `QualifiedQty` decimal(18,4) NOT NULL,
  `DefectQty` decimal(18,4) NOT NULL,
  `DefectItemId` bigint DEFAULT NULL,
  `RequestedBy` bigint NOT NULL,
  `JudgeRemark` varchar(500) CHARACTER SET utf8mb4 COLLATE utf8mb4_0900_ai_ci DEFAULT NULL,
  `JobStatus` int NOT NULL,
  `Attempts` int NOT NULL,
  `Error` varchar(500) CHARACTER SET utf8mb4 COLLATE utf8mb4_0900_ai_ci DEFAULT NULL,
  `CompletedAt` datetime(6) DEFAULT NULL,
  `MessageId` char(36) CHARACTER SET ascii COLLATE ascii_general_ci NOT NULL,
  `TenantId` bigint DEFAULT NULL,
  `Code` varchar(500) CHARACTER SET utf8mb4 COLLATE utf8mb4_0900_ai_ci DEFAULT NULL,
  `Name` varchar(500) CHARACTER SET utf8mb4 COLLATE utf8mb4_0900_ai_ci DEFAULT NULL,
  `Status` int NOT NULL,
  `Remark` varchar(500) CHARACTER SET utf8mb4 COLLATE utf8mb4_0900_ai_ci DEFAULT NULL,
  `CreatedAt` datetime(6) NOT NULL,
  `CreatedBy` bigint DEFAULT NULL,
  `UpdatedAt` datetime(6) DEFAULT NULL,
  `UpdatedBy` bigint DEFAULT NULL,
  `IsDeleted` tinyint(1) NOT NULL,
  PRIMARY KEY (`Id`),
  UNIQUE KEY `IX_QualityJudgeJob_TenantId_IdempotencyKey` (`TenantId`,`IdempotencyKey`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

CREATE TABLE IF NOT EXISTS `SubcontractOrder` (
  `Id` bigint NOT NULL AUTO_INCREMENT,
  `SupplierId` bigint NOT NULL,
  `InputProductId` bigint NOT NULL,
  `OutputProductId` bigint NOT NULL,
  `OrderQty` decimal(18,4) NOT NULL,
  `IssuedQty` decimal(18,4) NOT NULL,
  `ReceivedQty` decimal(18,4) NOT NULL,
  `SourceWarehouseId` bigint NOT NULL,
  `TargetWarehouseId` bigint NOT NULL,
  `OrderStatus` int NOT NULL,
  `OrderDate` datetime(6) NOT NULL,
  `DueDate` datetime(6) DEFAULT NULL,
  `InputBatchNo` varchar(500) CHARACTER SET utf8mb4 COLLATE utf8mb4_0900_ai_ci DEFAULT NULL,
  `InputLocation` varchar(500) CHARACTER SET utf8mb4 COLLATE utf8mb4_0900_ai_ci DEFAULT NULL,
  `OutputBatchNo` varchar(500) CHARACTER SET utf8mb4 COLLATE utf8mb4_0900_ai_ci DEFAULT NULL,
  `OutputLocation` varchar(500) CHARACTER SET utf8mb4 COLLATE utf8mb4_0900_ai_ci DEFAULT NULL,
  `TenantId` bigint DEFAULT NULL,
  `Code` varchar(100) CHARACTER SET utf8mb4 COLLATE utf8mb4_0900_ai_ci DEFAULT NULL,
  `Name` varchar(500) CHARACTER SET utf8mb4 COLLATE utf8mb4_0900_ai_ci DEFAULT NULL,
  `Status` int NOT NULL,
  `Remark` varchar(500) CHARACTER SET utf8mb4 COLLATE utf8mb4_0900_ai_ci DEFAULT NULL,
  `CreatedAt` datetime(6) NOT NULL,
  `CreatedBy` bigint DEFAULT NULL,
  `UpdatedAt` datetime(6) DEFAULT NULL,
  `UpdatedBy` bigint DEFAULT NULL,
  `IsDeleted` tinyint(1) NOT NULL,
  PRIMARY KEY (`Id`),
  UNIQUE KEY `IX_SubcontractOrder_TenantId_Code` (`TenantId`,`Code`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;
-- Existing StockBill.Code can remain varchar(500); MySQL utf8mb4 index fits.
-- Duplicate tenant/code pairs make this ALTER fail safely rather than silently truncating.
SET @smes_ddl = (SELECT IF(COUNT(*)=0,
  'ALTER TABLE `StockBill` ADD UNIQUE INDEX `IX_StockBill_TenantId_Code` (`TenantId`,`Code`)',
  'SELECT 1') FROM information_schema.STATISTICS
  WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME='StockBill' AND INDEX_NAME='IX_StockBill_TenantId_Code');
PREPARE smes_stmt FROM @smes_ddl;
EXECUTE smes_stmt;
DEALLOCATE PREPARE smes_stmt;
