-- Optional demo: run manually in an empty, disposable test database you choose.
-- This creates tables only. It does not create/drop databases or include real data.
-- If these names already exist, use a different empty database.
CREATE TABLE dbo.Customers (
    CustomerId int NOT NULL PRIMARY KEY,
    DisplayName nvarchar(100) NOT NULL
);
CREATE TABLE dbo.Orders (
    OrderId int NOT NULL PRIMARY KEY,
    CustomerId int NOT NULL,
    CONSTRAINT FK_Orders_Customers FOREIGN KEY (CustomerId)
        REFERENCES dbo.Customers(CustomerId)
);
CREATE TABLE dbo.OrderItems (
    OrderItemId int NOT NULL PRIMARY KEY,
    OrderId int NOT NULL,
    ItemName nvarchar(100) NOT NULL,
    CONSTRAINT FK_OrderItems_Orders FOREIGN KEY (OrderId)
        REFERENCES dbo.Orders(OrderId)
);
CREATE TABLE dbo.OrderItemDetails (
    OrderItemDetailId int NOT NULL PRIMARY KEY,
    OrderItemId int NOT NULL,
    Detail nvarchar(100) NULL,
    CONSTRAINT FK_OrderItemDetails_OrderItems FOREIGN KEY (OrderItemId)
        REFERENCES dbo.OrderItems(OrderItemId)
);
GO
-- In a new query tab connected to the same database, type these progressively:
-- SELECT * FROM Ord
-- SELECT * FROM Orders INNER JOIN
-- SELECT * FROM Orders INNER JOIN OrderItems ON
-- SELECT Orders. FROM Orders
