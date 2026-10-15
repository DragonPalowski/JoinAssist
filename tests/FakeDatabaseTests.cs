using System;
using System.Collections;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Linq;

internal sealed class FakeDatabase : DbConnection
{
    ConnectionState state;
    public readonly List<FakeCommand> Commands = new List<FakeCommand>();
    public bool MissingTable;
    public override string ConnectionString { get; set; }
    public override string Database { get { return "TestDb"; } }
    public override string DataSource { get { return "TestServer"; } }
    public override string ServerVersion { get { return "16.0"; } }
    public override ConnectionState State { get { return state; } }
    public override void Open() { state = ConnectionState.Open; }
    public override void Close() { state = ConnectionState.Closed; }
    public override void ChangeDatabase(string name) { throw new NotSupportedException(); }
    protected override DbTransaction BeginDbTransaction(IsolationLevel isolationLevel) { throw new NotSupportedException(); }
    protected override DbCommand CreateDbCommand() { var cmd = new FakeCommand(this); Commands.Add(cmd); return cmd; }
    public DataTable Result(FakeCommand cmd)
    {
        if (!cmd.CommandText.TrimStart().StartsWith("SELECT",StringComparison.OrdinalIgnoreCase)) throw new Exception("Il reader deve eseguire solo SELECT.");
        var table = new DataTable();
        if (cmd.CommandText.Contains("FROM sys.objects AS o"))
        {
            Type[] types={typeof(int),typeof(string),typeof(string),typeof(string),typeof(bool),typeof(string)};
            for(int i=0;i<types.Length;i++) table.Columns.Add("c"+i,types[i]);
            if (!MissingTable) table.Rows.Add(1,"dbo","Orders","U",true,DBNull.Value);
            table.Rows.Add(2,"crm","Customers","U",true,DBNull.Value);
            table.Rows.Add(3,"dbo","Items","U",true,DBNull.Value);
        }
        else if (cmd.CommandText.Contains("FROM sys.columns AS c"))
        {
            table.Columns.Add("name",typeof(string));table.Columns.Add("type",typeof(string));
            int id=(int)cmd.Parameters["@objectId"].Value;
            string[] names=id==1?new[]{"Id","CustomerId","TenantId"}:id==2?new[]{"Id","TenantId","Name"}:new[]{"Id","OrderId"};
            foreach(var name in names)table.Rows.Add(name,"int");
        }
        else
        {
            Type[] types={typeof(int),typeof(string),typeof(int),typeof(int),typeof(string),typeof(string),typeof(string),typeof(string),typeof(bool),typeof(bool),typeof(int),typeof(string),typeof(string)};
            for(int i=0;i<types.Length;i++) table.Columns.Add("c"+i,types[i]);
            table.Rows.Add(100,"FK_Orders_Customers",1,2,"dbo","Orders","crm","Customers",false,false,1,"CustomerId","Id");
            table.Rows.Add(100,"FK_Orders_Customers",1,2,"dbo","Orders","crm","Customers",false,false,2,"TenantId","TenantId");
            table.Rows.Add(101,"FK_Items_Orders",3,1,"dbo","Items","dbo","Orders",false,true,1,"OrderId","Id");
        }
        return table;
    }
}
internal sealed class FakeCommand : DbCommand
{
    readonly FakeDatabase owner;
    readonly FakeParameters parameters = new FakeParameters();
    public FakeCommand(FakeDatabase database) {owner=database;}
    public override string CommandText {get;set;}
    public override int CommandTimeout {get;set;}
    public override CommandType CommandType {get;set;}
    public override bool DesignTimeVisible {get;set;}
    public override UpdateRowSource UpdatedRowSource {get;set;}
    protected override DbConnection DbConnection {get {return owner;}set {throw new NotSupportedException();}}
    protected override DbParameterCollection DbParameterCollection {get {return parameters;}}
    protected override DbTransaction DbTransaction {get;set;}
    public override void Cancel() {}
    public override int ExecuteNonQuery() {throw new Exception("Non deve essere eseguito DML.");}
    public override object ExecuteScalar() {throw new NotSupportedException();}
    public override void Prepare() {}
    protected override DbParameter CreateDbParameter() {return new FakeParameter();}
    protected override DbDataReader ExecuteDbDataReader(CommandBehavior behavior) {return owner.Result(this).CreateDataReader();}
}
internal sealed class FakeParameter : DbParameter
{
    public override DbType DbType {get;set;}
    public override ParameterDirection Direction {get;set;}
    public override bool IsNullable {get;set;}
    public override string ParameterName {get;set;}
    public override string SourceColumn {get;set;}
    public override object Value {get;set;}
    public override bool SourceColumnNullMapping {get;set;}
    public override int Size {get;set;}
    public override void ResetDbType() {}
}
internal sealed class FakeParameters : DbParameterCollection
{
    readonly List<DbParameter> items=new List<DbParameter>();
    public override int Count {get {return items.Count;}}
    public override object SyncRoot {get {return ((ICollection)items).SyncRoot;}}
    public override int Add(object value) {items.Add((DbParameter)value);return items.Count-1;}
    public override void AddRange(Array values) {foreach(var value in values) Add(value);}
    public override void Clear() {items.Clear();}
    public override bool Contains(object value) {return items.Contains((DbParameter)value);}
    public override bool Contains(string value) {return IndexOf(value)>=0;}
    public override void CopyTo(Array array,int index) {((ICollection)items).CopyTo(array,index);}
    public override IEnumerator GetEnumerator() {return items.GetEnumerator();}
    public override int IndexOf(object value) {return items.IndexOf((DbParameter)value);}
    public override int IndexOf(string value) {return items.FindIndex(p=>p.ParameterName==value);}
    public override void Insert(int index,object value) {items.Insert(index,(DbParameter)value);}
    public override void Remove(object value) {items.Remove((DbParameter)value);}
    public override void RemoveAt(int index) {items.RemoveAt(index);}
    public override void RemoveAt(string name) {items.RemoveAt(IndexOf(name));}
    protected override DbParameter GetParameter(int index) {return items[index];}
    protected override DbParameter GetParameter(string name) {return items[IndexOf(name)];}
    protected override void SetParameter(int index,DbParameter value) {items[index]=value;}
    protected override void SetParameter(string name,DbParameter value) {items[IndexOf(name)]=value;}
}
