using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace JoinAssist
{
    public sealed class ExecutionFinding
    {
        public string Kind,Target;
        public int Offset,Length,Line;
        public override string ToString(){return Kind+" senza WHERE · "+Target+" · riga "+Line;}
    }
    public sealed class ExecutionReview
    {
        public readonly List<ExecutionFinding> Findings=new List<ExecutionFinding>();
        public string Uncertainty;
        public bool NeedsConfirmation {get{return Findings.Count>0 || Uncertainty!=null;}}
    }
    public static class ExecutionSafety
    {
        public static ExecutionReview Analyze(string sql,int baseOffset=0,int firstLine=1)
        {
            var result=new ExecutionReview();
            if(sql==null || sql.Length>500000){result.Uncertainty="Testo assente o superiore a 500.000 caratteri: controllo non completato.";return result;}
            IList<ParseError> errors;
            var tree=new TSql160Parser(true).Parse(new StringReader(sql),out errors);
            if(errors.Count>0)result.Uncertainty="Il testo contiene sintassi non riconosciuta o incompleta: il controllo potrebbe non rilevare tutte le istruzioni.";
            tree.Accept(new Visitor(result,baseOffset,firstLine));return result;
        }
        // A confirmation is valid for exactly the captured text/selection/editor context.
        public static bool Allow(ExecutionReview review,Func<ExecutionReview,bool> confirm,Func<bool> stillCurrent)
        {
            if(review.NeedsConfirmation && !confirm(review))return false;
            return stillCurrent();
        }
        sealed class Visitor : TSqlFragmentVisitor
        {
            readonly ExecutionReview review;readonly int offset,line;
            internal Visitor(ExecutionReview value,int baseOffset,int firstLine){review=value;offset=baseOffset;line=firstLine;}
            void Add(string kind,DataModificationSpecification spec)
            {
                if(spec==null || spec.Target==null){review.Uncertainty="Istruzione incompleta: destinazione non identificabile.";return;}
                var update=spec as UpdateSpecification;var delete=spec as DeleteSpecification;
                if(update!=null && update.WhereClause!=null || delete!=null && delete.WhereClause!=null)return;
                string target;new Sql160ScriptGenerator().GenerateScript(spec.Target,out target);
                target=target??"Destinazione non risolta";
                if(target.Length>180)target=target.Substring(0,180)+"…";
                review.Findings.Add(new ExecutionFinding{Kind=kind,Target=target,Offset=offset+spec.StartOffset,Length=spec.FragmentLength,Line=line+spec.StartLine-1});
            }
            public override void ExplicitVisit(UpdateStatement node){Add("UPDATE",node.UpdateSpecification);base.ExplicitVisit(node);}
            public override void ExplicitVisit(DeleteStatement node){Add("DELETE",node.DeleteSpecification);base.ExplicitVisit(node);}
            public override void ExplicitVisit(CreateProcedureStatement node){}
            public override void ExplicitVisit(AlterProcedureStatement node){}
            public override void ExplicitVisit(CreateOrAlterProcedureStatement node){}
            public override void ExplicitVisit(CreateFunctionStatement node){}
            public override void ExplicitVisit(AlterFunctionStatement node){}
            public override void ExplicitVisit(CreateOrAlterFunctionStatement node){}
            public override void ExplicitVisit(CreateTriggerStatement node){}
            public override void ExplicitVisit(AlterTriggerStatement node){}
            public override void ExplicitVisit(CreateOrAlterTriggerStatement node){}
        }
    }
}
