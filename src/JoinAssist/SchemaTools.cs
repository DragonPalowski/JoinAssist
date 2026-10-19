using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace JoinAssist
{
    public sealed class StarExpansion
    {
        public QueryContext Context;
        public string[] Qualifier;
        public static StarExpansion Create(string sql,int caret)
        {
            SqlAnalysis.CheckCaret(sql,caret);
            var index=SqlAnalysis.ParseIndex(sql);
            var scope=index.Scopes.Where(s=>s.Query.StartOffset<=caret && caret<=s.Query.StartOffset+s.Query.FragmentLength).OrderBy(s=>s.Query.FragmentLength).FirstOrDefault();
            var star=scope==null?null:scope.Query.SelectElements.OfType<SelectStarExpression>().FirstOrDefault(s=>s.StartOffset<=caret && caret<=s.StartOffset+s.FragmentLength);
            if(star==null)throw new InvalidOperationException("Posiziona il cursore sull'asterisco di SELECT * oppure tabella.*. COUNT(*) non è una proiezione espandibile.");
            var context=SqlAnalysis.ReadScope(sql,star.StartOffset);
            context.ReplaceStart=star.StartOffset;context.ReplaceLength=star.FragmentLength;
            return new StarExpansion{Context=context,Qualifier=star.Qualifier==null?new string[0]:star.Qualifier.Identifiers.Select(i=>i.Value).ToArray()};
        }
        public async Task<List<ColumnChoice>> LoadAsync(DbConnection connection,CancellationToken cancel,MetadataScope cache=null)
        {
            var catalog=await CatalogReader.ReadAsync(connection,cancel,cache).ConfigureAwait(false);
            await new ShapeResolver(catalog,cancel).ResolveAsync(Context,false).ConfigureAwait(false);
            var tables=Qualifier.Length==0?Context.Tables:ShapeResolver.MatchTables(Context.Tables,Qualifier);
            if(tables.Count==0 || Qualifier.Length>0 && tables.Count!=1 || tables.Any(t=>t.Columns==null || t.Columns.Count==0 || t.Columns.GroupBy(c=>c.Name,StringComparer.OrdinalIgnoreCase).Any(g=>g.Count()>1)))
                throw new InvalidOperationException("Le colonne dell'asterisco non sono risolvibili senza ambiguità. Nessuna modifica eseguita.");
            var columns=tables.SelectMany(t=>t.Columns.Select(c=>new ColumnChoice{Sql=t.Reference+"."+SqlName.Format(c.Name),Description=c.Description,Selected=true})).ToList();
            if(columns.Count>2000)throw new InvalidOperationException("Più di 2.000 colonne: espandi una tabella alla volta.");
            return columns;
        }
        public static string Text(IEnumerable<ColumnChoice> choices)
        {var selected=choices.Where(c=>c.Selected).Select(c=>c.Sql).ToList();if(selected.Count==0)throw new InvalidOperationException("Seleziona almeno una colonna.");return string.Join(", ",selected);}
    }
    public sealed class ColumnChoice
    {
        public bool Selected {get;set;}
        public string Sql {get;set;}
        public string Description {get;set;}
    }
    public sealed class ObjectInfo
    {
        public string Name {get;set;}
        public List<ColumnChoice> Columns {get;set;}
        public string Relationships {get;set;}
    }
    public static class SchemaInspector
    {
        public static string[] ReferenceAt(string sql,int caret)
        {
            var tokens=SqlAnalysis.Tokens(sql).Where(SqlAnalysis.Significant).ToList();
            int i=tokens.FindIndex(t=>t.Offset<=caret && caret<=t.Offset+t.Text.Length);
            if(i<0)return new string[0];
            if(tokens[i].TokenType==TSqlTokenType.Dot && i>0)i--;
            if(!IsName(tokens[i]))return new string[0];
            int first=i,last=i;
            while(first>=2 && tokens[first-1].TokenType==TSqlTokenType.Dot && IsName(tokens[first-2]))first-=2;
            while(last+2<tokens.Count && tokens[last+1].TokenType==TSqlTokenType.Dot && IsName(tokens[last+2]))last+=2;
            return tokens.Skip(first).Take(last-first+1).Where(IsName).Select(t=>SqlName.Decode(t.Text)).ToArray();
        }
        static bool IsName(TSqlParserToken t){return t.TokenType==TSqlTokenType.Identifier || t.TokenType==TSqlTokenType.QuotedIdentifier || t.TokenType==TSqlTokenType.Variable;}
        public static async Task<List<ObjectInfo>> LoadAsync(DbConnection connection,QueryContext context,string[] reference,CancellationToken cancel,MetadataScope cache=null)
        {
            var catalog=await CatalogReader.ReadAsync(connection,cancel,cache).ConfigureAwait(false);
            var uses=context.Tables.Concat(context.OuterTables.Where(o=>!context.Tables.Any(t=>string.Equals(t.Alias??t.Name,o.Alias??o.Name,StringComparison.OrdinalIgnoreCase)))).ToList();
            foreach(var use in uses.Where(t=>t.Definition==null && !t.IsLocal)){var obj=catalog.Find(use.Schema,use.Name);if(obj!=null)use.ResolvedSchema=obj.Schema;}
            string column=null;
            if(reference.Length>0)
            {
                var exact=ShapeResolver.MatchTables(uses,reference);
                if(exact.Count==0 && reference.Length>1){exact=ShapeResolver.MatchTables(uses,reference.Take(reference.Length-1).ToArray());if(exact.Count>0)column=reference.Last();}
                if(exact.Count==0 && reference.Length<=2)
                {
                    CommonTableExpression cte;
                    if(reference.Length==1 && context.Ctes.TryGetValue(reference[0],out cte))exact.Add(SqlAnalysis.CteUse(reference[0],null,cte,context.Ctes));
                    else
                    {
                        var obj=catalog.Find(reference.Length==2?reference[0]:null,reference.Last());
                        if(obj!=null)exact.Add(new TableUse{Name=obj.Name,Schema=obj.Schema});
                    }
                }
                if(exact.Count>1)throw new InvalidOperationException("Riferimento ambiguo: seleziona un alias o un nome con schema.");
                if(exact.Count==1)uses=exact;
                else if(reference.Length==1)column=reference[0];
                else throw new InvalidOperationException("Riferimento non risolvibile nel database e nell'ambito correnti.");
            }
            var focused=new QueryContext{Locals=context.Locals};focused.Tables.AddRange(uses);
            await new ShapeResolver(catalog,cancel).ResolveAsync(focused,false).ConfigureAwait(false);
            if(column!=null)uses=uses.Where(t=>(t.Columns??new List<ColumnShape>()).Any(c=>string.Equals(c.Name,column,StringComparison.OrdinalIgnoreCase))).ToList();
            if(column!=null && uses.Count!=1)throw new InvalidOperationException("Colonna assente o ambigua nell'ambito corrente.");
            var keys=await MetadataReader.ReadForeignKeysAsync(connection,focused,catalog,cancel,cache).ConfigureAwait(false);
            return uses.Select(t=>new ObjectInfo{Name=t.Reference+(t.IsVirtual?" · sorgente derivata":""),
                Columns=(t.Columns??new List<ColumnShape>()).Where(c=>column==null || string.Equals(c.Name,column,StringComparison.OrdinalIgnoreCase)).Select(c=>new ColumnChoice{Sql=c.Name,Description=c.Description}).ToList(),
                Relationships=Relationships(t,keys)}).ToList();
        }
        static string Relationships(TableUse table,List<ForeignKey> keys)
        {
            // Derived sources can expose renamed/partial columns; show only fully mapped real constraints.
            var relations=new List<string>();
            foreach(var map in table.Maps)
                foreach(var fk in keys.Where(k=>k.ChildId==map.ObjectId || k.ParentId==map.ObjectId))
                {
                    bool child=fk.ChildId==map.ObjectId;
                    if(fk.Columns.Any(c=>map.Column(child?c.Child:c.Parent)==null))continue;
                    relations.Add(fk.Name+(fk.Disabled?" · disabilitata":fk.Untrusted?" · non verificata":"")+Environment.NewLine+
                        string.Join(" AND ",fk.Columns.OrderBy(c=>c.Ordinal).Select(c=>table.Reference+"."+SqlName.Format(map.Column(child?c.Child:c.Parent))+" → "+SqlName.Format(child?fk.ParentSchema:fk.ChildSchema)+"."+SqlName.Format(child?fk.ParentTable:fk.ChildTable)+"."+SqlName.Format(child?c.Parent:c.Child))));
                }
            return relations.Count==0?"Nessuna relazione FK visibile e risolvibile per questa sorgente.":string.Join(Environment.NewLine+Environment.NewLine,relations.Distinct());
        }
    }
}
