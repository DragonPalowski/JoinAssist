using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace JoinAssist
{
    // Static descriptions only. Expressions never inherit FK provenance or writeability.
    internal static class ExpressionTypes
    {
        internal static ColumnShape Infer(ScalarExpression expression,IList<TableUse> tables,int depth=0)
        {
            if(expression==null || depth>24)return null;
            var column=expression as ColumnReferenceExpression;
            if(column!=null && column.MultiPartIdentifier!=null)
            {
                var ids=column.MultiPartIdentifier.Identifiers.Select(i=>i.Value).ToArray();
                var sources=ids.Length==1?tables:ShapeResolver.MatchTables(tables,ids.Take(ids.Length-1).ToArray());
                var matches=sources.SelectMany(t=>t.Columns??new List<ColumnShape>()).Where(c=>string.Equals(c.Name,ids.Last(),StringComparison.OrdinalIgnoreCase)).ToList();
                return matches.Count==1?matches[0].Copy():null;
            }
            var parentheses=expression as ParenthesisExpression;
            if(parentheses!=null)return Infer(parentheses.Expression,tables,depth+1);
            var cast=expression as CastCall;
            if(cast!=null)return Declared(cast.DataType);
            var convert=expression as ConvertCall;
            if(convert!=null)return Declared(convert.DataType);
            var tryCast=expression as TryCastCall;
            if(tryCast!=null)return Declared(tryCast.DataType);
            var tryConvert=expression as TryConvertCall;
            if(tryConvert!=null)return Declared(tryConvert.DataType);
            if(expression is NullLiteral)return new ColumnShape{Nullable=true,Computed=true};
            var integer=expression as IntegerLiteral;
            if(integer!=null) { int value;return new ColumnShape{TypeName=int.TryParse(integer.Value,out value)?"int":"decimal("+integer.Value.TrimStart('0').Length+",0)",Nullable=false,Computed=true}; }
            var str=expression as StringLiteral;
            if(str!=null)
            {
                string type=str.IsNational?"nvarchar":"varchar";
                if(str.IsNational || str.Value.All(c=>c<128))type+="("+(str.Value.Length>(str.IsNational?4000:8000)?"max":Math.Max(1,str.Value.Length).ToString(System.Globalization.CultureInfo.InvariantCulture))+")";
                return new ColumnShape{TypeName=type,Nullable=false,Computed=true};
            }
            var numeric=expression as NumericLiteral;
            if(numeric!=null)
            {
                var parts=numeric.Value.Split('.');int scale=parts.Length==2?parts[1].Length:0;
                int precision=Math.Max(1,parts[0].TrimStart('0').Length+scale);if(precision>38)return null;
                return new ColumnShape{TypeName="decimal("+precision+","+scale+")",Nullable=false,Computed=true};
            }
            if(expression is RealLiteral)return new ColumnShape{TypeName="float",Nullable=false,Computed=true};
            var searched=expression as SearchedCaseExpression;
            if(searched!=null)return Merge(searched.WhenClauses.Select(w=>Infer(w.ThenExpression,tables,depth+1)).Concat(new[]{searched.ElseExpression==null?new ColumnShape{Nullable=true}:Infer(searched.ElseExpression,tables,depth+1)}));
            var simple=expression as SimpleCaseExpression;
            if(simple!=null)return Merge(simple.WhenClauses.Select(w=>Infer(w.ThenExpression,tables,depth+1)).Concat(new[]{simple.ElseExpression==null?new ColumnShape{Nullable=true}:Infer(simple.ElseExpression,tables,depth+1)}));
            var coalesce=expression as CoalesceExpression;
            if(coalesce!=null) {var value=Merge(coalesce.Expressions.Select(e=>Infer(e,tables,depth+1)));if(value!=null)value.Nullable=null;return value;}
            var function=expression as FunctionCall;
            if(function!=null && function.CallTarget==null)
            {
                string name=function.FunctionName.Value.ToUpperInvariant();
                if(name=="COUNT" || name=="COUNT_BIG" || name=="ROW_NUMBER" || name=="RANK" || name=="DENSE_RANK")return new ColumnShape{TypeName=name=="COUNT"?"int":"bigint",Computed=true};
                if(name=="ISNULL" && function.Parameters.Count==2)
                {var first=Infer(function.Parameters[0],tables,depth+1);if(first!=null && first.TypeName!=null)return Derived(first);}
            }
            return null;
        }
        static ColumnShape Declared(DataTypeReference type)
        {string text;new Sql160ScriptGenerator().GenerateScript(type,out text);return new ColumnShape{TypeName=type is SqlDataTypeReference?System.Text.RegularExpressions.Regex.Replace(text,@"\s+","").ToLowerInvariant():text,Computed=true};}
        static ColumnShape Derived(ColumnShape shape)
        {var result=shape.Copy();result.BaseObjectId=0;result.BaseColumn=result.Instance=null;result.Identity=result.PrimaryKey=result.ForeignKey=null;result.Nullable=null;result.Computed=true;return result;}
        static ColumnShape Merge(IEnumerable<ColumnShape> values)
        {
            var all=values.ToList();if(all.Any(c=>c==null))return null;
            var typed=all.Where(c=>c.TypeName!=null).ToList();if(typed.Count==0)return new ColumnShape{Nullable=true,Computed=true};
            // Different SQL types/lengths require SQL Server coercion rules; leave those unknown.
            if(typed.Any(c=>!string.Equals(c.Description.Split(new[]{" · "},StringSplitOptions.None)[0],typed[0].Description.Split(new[]{" · "},StringSplitOptions.None)[0],StringComparison.OrdinalIgnoreCase)))return null;
            var result=Derived(typed[0]);result.Nullable=all.Any(c=>c.Nullable==true)?true:all.All(c=>c.Nullable==false)?(bool?)false:null;return result;
        }
    }
}
