-- SQL Server 2022+. Run in SSMS or with sqlcmd (GO is a client batch separator).
-- This script is idempotent and preserves existing reviews and descriptions.
IF DB_ID(N'SMARTSHOP') IS NULL
    CREATE DATABASE [SMARTSHOP];
GO
USE [SMARTSHOP];
GO
IF OBJECT_ID(N'dbo.Product', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.Product (
        ProductId int NOT NULL CONSTRAINT PK_Product PRIMARY KEY,
        ProductName nvarchar(128) NOT NULL,
        Category nvarchar(64) NOT NULL,
        Price decimal(18,2) NOT NULL,
        Features nvarchar(512) NOT NULL,
        Description nvarchar(max) NULL,
        CONSTRAINT CK_Product_Price CHECK (Price >= 0)
    );
END;
GO
IF OBJECT_ID(N'dbo.Review', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.Review (
        ReviewId int IDENTITY(1,1) NOT NULL CONSTRAINT PK_Review PRIMARY KEY,
        ProductId int NOT NULL,
        CustomerName nvarchar(64) NOT NULL,
        Content nvarchar(1000) NOT NULL,
        Rating int NOT NULL,
        Sentiment nvarchar(16) NOT NULL,
        Score float NOT NULL,
        Summary nvarchar(256) NOT NULL,
        CreatedAt datetime NOT NULL CONSTRAINT DF_Review_CreatedAt DEFAULT GETUTCDATE(),
        CONSTRAINT FK_Review_Product FOREIGN KEY (ProductId) REFERENCES dbo.Product(ProductId),
        CONSTRAINT CK_Review_Rating CHECK (Rating BETWEEN 1 AND 5),
        CONSTRAINT CK_Review_Sentiment CHECK (Sentiment IN (N'Positive', N'Negative', N'Neutral')),
        CONSTRAINT CK_Review_Score CHECK (Score >= 0 AND Score <= 1)
    );
    CREATE INDEX IX_Review_ProductId ON dbo.Review(ProductId);
END;
GO
INSERT INTO dbo.Product (ProductId, ProductName, Category, Price, Features)
SELECT seed.* FROM (VALUES
    (1, N'Galaxy S25', N'Phone', CAST(22990000 AS decimal(18,2)), N'6.2 inch AMOLED, Snapdragon 8 Elite, 50MP camera'),
    (2, N'MacBook Air M4', N'Laptop', 28490000, N'13.6 inch, 16GB RAM, 18 hours battery'),
    (3, N'AirPods Pro 3', N'Accessory', 6790000, N'Active noise cancellation, USB C, IP57'),
    (4, N'Xiaomi Redmi Note 15', N'Phone', 6490000, N'120Hz display, 5500mAh battery, 108MP camera')
) AS seed (ProductId, ProductName, Category, Price, Features)
WHERE NOT EXISTS (SELECT 1 FROM dbo.Product p WHERE p.ProductId = seed.ProductId);
GO

-- The report includes products with no reviews (average = 0).
SELECT ROW_NUMBER() OVER (ORDER BY p.ProductId) AS [No], p.ProductName,
    COUNT(r.ReviewId) AS TotalReviews,
    SUM(CASE WHEN r.Sentiment = N'Positive' THEN 1 ELSE 0 END) AS Positive,
    SUM(CASE WHEN r.Sentiment = N'Negative' THEN 1 ELSE 0 END) AS Negative,
    SUM(CASE WHEN r.Sentiment = N'Neutral' THEN 1 ELSE 0 END) AS Neutral,
    CAST(COALESCE(AVG(CAST(r.Rating AS float)), 0) AS decimal(3,1)) AS AverageRating
FROM dbo.Product p LEFT JOIN dbo.Review r ON r.ProductId = p.ProductId
GROUP BY p.ProductId, p.ProductName ORDER BY p.ProductId;
GO
