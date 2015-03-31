﻿/********************************************************************++
Copyright (c) Microsoft Corporation.  All rights reserved.
--********************************************************************/

using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Concurrent;
using System.Linq;
using System.Reflection;
using System.Management.Automation.Language;
using System.Management.Automation;
using System.Globalization;

namespace Microsoft.Windows.Powershell.ScriptAnalyzer
{
    /// <summary>
    /// Base class for variable details
    /// </summary>
    public class VariableDetails
    {
        /// <summary>
        /// Type of variable
        /// </summary>
        public Type Type = typeof(Unreached);

        /// <summary>
        /// Name of variable (can be set by ssa)
        /// </summary>
        public string Name;

        /// <summary>
        /// Real name of variable
        /// </summary>
        public string RealName;

        /// <summary>
        /// Value of variable
        /// </summary>
        public object Constant = Unreached.UnreachedConstant;

        /// <summary>
        /// Block that variable is initialized in
        /// </summary>
        public Block DefinedBlock;
    }

    /// <summary>
    /// Class that stores the details of variable analysis
    /// </summary>
    public class VariableAnalysisDetails : VariableDetails
    {
        internal VariableAnalysisDetails()
        {
            this.AssociatedAsts = new List<Ast>();
        }

        /// <summary>
        /// True if variable is initialized
        /// </summary>
        public bool Uninitialized { get; set; }

        /// <summary>
        /// The Asts associated with the variables
        /// </summary>
        public List<Ast> AssociatedAsts { get; internal set; }

        internal List<Block> AssignedBlocks = new List<Block>();
    }

    /// <summary>
    /// This class is used to find all the variables in an ast
    /// </summary>
    public class FindAllVariablesVisitor : AstVisitor
    {
        /// <summary>
        /// Visit an Ast and gets details about all the variables
        /// </summary>
        /// <param name="ast"></param>
        /// <returns></returns>
        public static Dictionary<string, VariableAnalysisDetails> Visit(Ast ast)
        {
            if (!(ast is ScriptBlockAst || ast is FunctionMemberAst || ast is FunctionDefinitionAst))
            {
                return null;
            }

            var visitor = new FindAllVariablesVisitor();

            // Visit the body before the parameters so we don't allocate any tuple slots for parameters
            // if we won't be optimizing because of a call to new-variable/remove-variable, etc.

            if (ast is ScriptBlockAst)
            {
                (ast as ScriptBlockAst).Visit(visitor);
            }
            else if (ast is FunctionMemberAst)
            {
                (ast as FunctionMemberAst).Body.Visit(visitor);
            }
            else if (ast is FunctionDefinitionAst)
            {
                (ast as FunctionDefinitionAst).Body.Visit(visitor);
            }

            if (ast is FunctionMemberAst && (ast as FunctionMemberAst).Parameters != null)
            {
                visitor.VisitParameters((ast as FunctionMemberAst).Parameters);
            }
            else if (ast is FunctionDefinitionAst && (ast as FunctionDefinitionAst).Parameters != null)
            {
                visitor.VisitParameters((ast as FunctionDefinitionAst).Parameters);
            }

            return visitor._variables;
        }

        internal readonly Dictionary<string, VariableAnalysisDetails> _variables
            = new Dictionary<string, VariableAnalysisDetails>(StringComparer.OrdinalIgnoreCase);

        internal void VisitParameters(ReadOnlyCollection<ParameterAst> parameters)
        {
            foreach (ParameterAst t in parameters)
            {
                var variableExpressionAst = t.Name;
                var varPath = variableExpressionAst.VariablePath;

                var variableName = AssignmentTarget.GetUnaliasedVariableName(varPath);
                VariableAnalysisDetails analysisDetails;
                if (_variables.TryGetValue(variableName, out analysisDetails))
                {
                    // Forget whatever type we deduced in the body, we'll revisit that type after walking
                    // the flow graph.  We should see the parameter type for the variable first.
                    analysisDetails.Type = t.StaticType;
                }
                else
                {
                    NoteVariable(variableName, t.StaticType);
                }
            }
        }

        /// <summary>
        /// Visit datastatement
        /// </summary>
        /// <param name="dataStatementAst"></param>
        /// <returns></returns>
        public override AstVisitAction VisitDataStatement(DataStatementAst dataStatementAst)
        {
            if (dataStatementAst != null && dataStatementAst.Variable != null)
            {
                NoteVariable(dataStatementAst.Variable, null);
            }

            return AstVisitAction.Continue;
        }

        /// <summary>
        /// Visit SwitchStatement
        /// </summary>
        /// <param name="switchStatementAst"></param>
        /// <returns></returns>
        public override AstVisitAction VisitSwitchStatement(SwitchStatementAst switchStatementAst)
        {
            NoteVariable(SpecialVars.@switch, typeof(IEnumerator));

            return AstVisitAction.Continue;
        }

        /// <summary>
        /// Visit Foreach statement
        /// </summary>
        /// <param name="forEachStatementAst"></param>
        /// <returns></returns>
        public override AstVisitAction VisitForEachStatement(ForEachStatementAst forEachStatementAst)
        {
            NoteVariable(SpecialVars.@foreach, typeof(IEnumerator));

            return AstVisitAction.Continue;
        }
         
        /// <summary>
        /// Visit VariableExpression
        /// </summary>
        /// <param name="variableExpressionAst"></param>
        /// <returns></returns>
        public override AstVisitAction VisitVariableExpression(VariableExpressionAst variableExpressionAst)
        {
            if (variableExpressionAst == null)
            {
                return AstVisitAction.Continue;
            }

            NoteVariable(AssignmentTarget.GetUnaliasedVariableName(variableExpressionAst.VariablePath), null);

            return AstVisitAction.Continue;
        }

        /// <summary>
        /// Visit UsingExpression
        /// </summary>
        /// <param name="usingExpressionAst"></param>
        /// <returns></returns>
        public override AstVisitAction VisitUsingExpression(UsingExpressionAst usingExpressionAst)
        {
            // On the local machine, we may have set the index because of a call to ScriptBlockToPowerShell or Invoke-Command.
            // On the remote machine, the index probably isn't set yet, so we set it here, mostly to avoid another pass
            // over the ast.  We assert below to ensure we're setting to the same value in both the local and remote cases.


            // Cannot access the RuntimeUsingIndex
            //if (usingExpressionAst.RuntimeUsingIndex == -1)
            //{
            //    usingExpressionAst.RuntimeUsingIndex = _runtimeUsingIndex;
            //}
            //System.Diagnostics.Debug.Assert(usingExpressionAst.RuntimeUsingIndex == _runtimeUsingIndex, "Logic error in visiting using expressions.");
            return AstVisitAction.Continue;
        }

        /// <summary>
        /// Visit Command
        /// </summary>
        /// <param name="commandAst"></param>
        /// <returns></returns>
        public override AstVisitAction VisitCommand(CommandAst commandAst)
        {
            return AstVisitAction.Continue;
        }

        /// <summary>
        /// Visit Function
        /// </summary>
        /// <param name="functionDefinitionAst"></param>
        /// <returns></returns>
        public override AstVisitAction VisitFunctionDefinition(FunctionDefinitionAst functionDefinitionAst)
        {
            // We don't want to discover any variables in nested functions - they get their own scope.
            return AstVisitAction.SkipChildren;
        }

        /// <summary>
        /// Visit ScriptBlock
        /// </summary>
        /// <param name="scriptBlockExpressionAst"></param>
        /// <returns></returns>
        public override AstVisitAction VisitScriptBlockExpression(ScriptBlockExpressionAst scriptBlockExpressionAst)
        {
            // We don't want to discover any variables in script block expressions - they get their own scope.
            return AstVisitAction.Continue;
        }

        /// <summary>
        /// Visit Trap
        /// </summary>
        /// <param name="trapStatementAst"></param>
        /// <returns></returns>
        public override AstVisitAction VisitTrap(TrapStatementAst trapStatementAst)
        {
            // We don't want to discover any variables in traps - they get their own scope.
            return AstVisitAction.SkipChildren;
        }

        // Return true if the variable is newly allocated and should be allocated in the locals tuple.
        internal void NoteVariable(string variableName, Type type)
        {
            if (!_variables.ContainsKey(variableName))
            {
                var details = new VariableAnalysisDetails
                {
                    Name = variableName,
                    Type = type
                };
                _variables.Add(variableName, details);
            }
        }
    }

    class LoopGotoTargets
    {
        internal LoopGotoTargets(string label, Block breakTarget, Block continueTarget)
        {
            this.Label = label;
            this.BreakTarget = breakTarget;
            this.ContinueTarget = continueTarget;
        }
        internal string Label { get; private set; }
        internal Block BreakTarget { get; private set; }
        internal Block ContinueTarget { get; private set; }
    }

    class Unreached
    {
        internal static object UnreachedConstant = new object();
    }

    class Undetermined
    {
        internal static object UndeterminedConstant = new object();
    }

    /// <summary>
    /// Class that represents a Phi function
    /// </summary>
    public class Phi : VariableDetails
    {
        internal VariableTarget[] Operands;

        /// <summary>
        /// Constructor
        /// </summary>
        /// <param name="VarName"></param>
        /// <param name="block"></param>
        internal Phi(String VarName, Block block)
        {
            Name = VarName;
            Operands = new VariableTarget[block._predecessors.Count()];
            for (int i = 0; i < Operands.Length; i += 1)
            {
                Operands[i] = new VariableTarget();
            }

            RealName = Name;
            DefinedBlock = block;
        }
    }

    /// <summary>
    /// A block in the data flow graph
    /// </summary>
    public class Block
    {
        /// <summary>
        /// Asts in the block
        /// </summary>
        public List<object> _asts = new List<object>();
        internal readonly List<Block> _successors = new List<Block>();

        /// <summary>
        /// Predecessor blocks
        /// </summary>
        public  List<Block> _predecessors = new List<Block>();
        internal readonly HashSet<Block> SSASuccessors = new HashSet<Block>();
        internal List<Block> DominatorSuccessors = new List<Block>();
        internal static int count;
        internal int PostOrder;
        internal HashSet<Block> dominanceFrontierSet = new HashSet<Block>();
        internal List<Phi> Phis = new List<Phi>();
        private static Dictionary<string, Stack<VariableAnalysisDetails>> SSADictionary = new Dictionary<string, Stack<VariableAnalysisDetails>>(StringComparer.OrdinalIgnoreCase);
        internal static Dictionary<string, VariableAnalysisDetails> VariablesDictionary;
        internal static Dictionary<string, VariableAnalysisDetails> InternalVariablesDictionary = new Dictionary<string, VariableAnalysisDetails>(StringComparer.OrdinalIgnoreCase);
        private static Dictionary<string, int> Counters = new Dictionary<string, int>();

        internal object _visitData;
        internal bool _visited;
        internal bool _throws;
        internal bool _returns;
        internal bool _unreachable;

        internal void FlowsTo(Block next)
        {
            if (_successors.IndexOf(next) < 0)
            {
                if (_unreachable)
                {
                    next._unreachable = true;
                }

                _successors.Add(next);
                next._predecessors.Add(this);
            }
        }

        internal void AddAst(object ast)
        {
            _asts.Add(ast);
        }

        /// <summary>
        /// Insert phi nodes at dominance frontier of each set.
        /// </summary>
        /// <param name="Variables"></param>
        internal static void InsertPhiNodes(Dictionary<string, VariableAnalysisDetails> Variables)
        {
            foreach (var variable in Variables.Keys.ToList())
            {
                List<Block> everOnWorkList = new List<Block>();
                List<Block> workList = new List<Block>();
                List<Block> hasPhiAlready = new List<Block>();

                everOnWorkList.AddRange(Variables[variable].AssignedBlocks);
                workList.AddRange(Variables[variable].AssignedBlocks);

                while (workList.Count != 0)
                {
                    Block block = workList[workList.Count - 1];
                    workList.RemoveAt(workList.Count - 1);
                    foreach (Block frontier in block.dominanceFrontierSet)
                    {
                        if (!hasPhiAlready.Contains(frontier))
                        {
                            // INSERT PHI FUNCTION
                            frontier.Phis.Add(new Phi(variable, frontier));

                            hasPhiAlready.Add(frontier);
                        }

                        if (!everOnWorkList.Contains(frontier))
                        {
                            workList.Add(frontier);
                            everOnWorkList.Add(frontier);
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Fill in the DominanceFrontiersSet of each block.
        /// </summary>
        /// <param name="Blocks"></param>
        /// <param name="Entry"></param>
        internal static void DominanceFrontiers(List<Block> Blocks, Block Entry)
        {
            Block[] dominators = SetDominators(Entry, Blocks);

            foreach (Block block in Blocks)
            {
                if (block._predecessors.Count >= 2)
                {
                    foreach (Block pred in block._predecessors)
                    {
                        Block runner = pred;
                        while (runner != dominators[block.PostOrder])
                        {
                            runner.dominanceFrontierSet.Add(block);
                            runner = dominators[block.PostOrder];
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Returns the immediate dominator of each block. The array returned is
        /// indexed by postorder number of each block.
        /// Based on http://www.cs.rice.edu/~keith/Embed/dom.pdf
        /// </summary>
        /// <param name="entry"></param>
        /// <param name="Blocks"></param>
        /// <returns></returns>
        internal static Block[] SetDominators(Block entry, List<Block> Blocks)
        {
            Block[] dominators = new Block[Blocks.Count];

            foreach (var block in Blocks)
            {
                dominators[block.PostOrder] = null;
            }

            dominators[entry.PostOrder] = entry;
            bool updated = true;

            while (updated)
            {
                updated = false;
                foreach (var block in Blocks)
                {
                    if (block == entry)
                    {
                        continue;
                    }

                    if (block._predecessors.Count == 0)
                    {
                        continue;
                    }

                    Block dom = block._predecessors[0];

                    // Get first proccessed node
                    foreach (var pred in block._predecessors)
                    {
                        if (dominators[pred.PostOrder] != null)
                        {
                            dom = pred;
                            break;
                        }
                    }

                    Block firstSelected = dom;

                    foreach (var pred in block._predecessors)
                    {
                        if (firstSelected != pred && dominators[pred.PostOrder] != null)
                        {
                            // The order is reversed so we have to subtract from total
                            dom = Blocks[Blocks.Count - Intersect(pred, dom, dominators) - 1];
                        }
                    }

                    if (dominators[block.PostOrder] != dom)
                    {
                        dominators[block.PostOrder] = dom;
                        updated = true;
                    }
                }
            }

            // Construct dominator tree
            foreach (var block in Blocks)
            {
                dominators[block.PostOrder].DominatorSuccessors.Add(block);
            }

            return dominators;
        }

        /// <summary>
        /// Given two nodes, return the postorder number of the closest node
        /// that dominates both.
        /// </summary>
        /// <param name="block1"></param>
        /// <param name="block2"></param>
        /// <param name="Dominators"></param>
        /// <returns></returns>
        internal static int Intersect(Block block1, Block block2, Block[] Dominators)
        {
            int b1 = block1.PostOrder;
            int b2 = block2.PostOrder;
            while (b1 != b2)
            {
                while (b1 < b2)
                {
                    b1 = Dominators[b1].PostOrder;
                }

                while (b2 < b1)
                {
                    b2 = Dominators[b2].PostOrder;
                }
            }

            return b1;
        }

        /// <summary>
        /// Generate new name for each assignment using the counters.
        /// </summary>
        /// <param name="VarName"></param>
        /// <param name="DefinedBlock"></param>
        /// <returns></returns>
        internal static string GenerateNewName(string VarName, Block DefinedBlock)
        {
            int i = Counters[VarName];
            Counters[VarName] += 1;

            string newName = String.Format(CultureInfo.CurrentCulture, "{0}{1}", VarName, i);
            var varAnalysis = new VariableAnalysisDetails { Name = newName, DefinedBlock = DefinedBlock, RealName = VarName };

            SSADictionary[VarName].Push(varAnalysis);
            InternalVariablesDictionary[newName] = varAnalysis;

            return newName;
        }

        /// <summary>
        /// Rename each variable (each assignment will give variable a new name).
        /// Also rename the phi function in each block as well
        /// </summary>
        /// <param name="block"></param>
        internal static void RenameVariables(Block block)
        {
            if (block._visited)
            {
                return;
            }

            block._visited = true;

            // foreach phi node generate new name
            foreach (var phi in block.Phis)
            {
                phi.Name = GenerateNewName(phi.RealName, block);
            }

            foreach (object ast in block._asts)
            {
                AssignmentTarget assignTarget = ast as AssignmentTarget;
                VariableTarget varTarget = null;

                if (assignTarget != null && !String.IsNullOrEmpty(assignTarget.Name))
                {
                    assignTarget.Name = GenerateNewName(assignTarget.Name, block);
                    varTarget = assignTarget._rightHandSideVariable;
                }

                varTarget = (varTarget != null) ? varTarget : ast as VariableTarget;

                if (varTarget != null)
                {
                    // If stack is empty then variable was not initialized;
                    if (SSADictionary[varTarget.RealName].Count == 0)
                    {
                        VariablesDictionary[VariableAnalysis.AnalysisDictionaryKey(varTarget.VarAst)] = new VariableAnalysisDetails { Name = varTarget.Name, DefinedBlock = null, Type = varTarget.Type, RealName = null };
                    }
                    else
                    {
                        VariableAnalysisDetails previous = SSADictionary[varTarget.RealName].Peek();
                        varTarget.Name = previous.Name;
                        if (previous.DefinedBlock != null)
                        {
                            previous.DefinedBlock.SSASuccessors.Add(block);
                        }

                        VariablesDictionary[VariableAnalysis.AnalysisDictionaryKey(varTarget.VarAst)] = previous;
                    }
                }
            }

            foreach (var successor in block._successors)
            {
                int index = successor._predecessors.IndexOf(block);
                foreach (var phi in successor.Phis)
                {
                    // Stacks may be empty when variable was not initialized on all paths;
                    if (SSADictionary[phi.RealName].Count != 0)
                    {
                        VariableAnalysisDetails previous = SSADictionary[phi.RealName].Peek();
                        if (previous.DefinedBlock != null)
                        {
                            previous.DefinedBlock.SSASuccessors.Add(successor);
                        }

                        phi.Operands[index] = new VariableTarget(previous);
                    }
                    else
                    {
                        phi.Operands[index] = new VariableTarget();
                        phi.Operands[index].Name = GenerateNewName(phi.RealName, null);
                    }
                }
            }

            // Preorder travel along the dominator tree
            foreach (var successor in block.DominatorSuccessors)
            {
                RenameVariables(successor);
            }

            foreach (var phi in block.Phis)
            {
                SSADictionary[phi.RealName].Pop();
            }

            foreach (object ast in block._asts)
            {
                AssignmentTarget assignTarget = ast as AssignmentTarget;
                if (assignTarget != null && !String.IsNullOrEmpty(assignTarget.RealName))
                {
                    SSADictionary[assignTarget.RealName].Pop();
                }
            }
        }

        /// <summary>
        /// Initialize SSA
        /// </summary>
        /// <param name="VariableAnalysis"></param>
        /// <param name="Entry"></param>
        /// <param name="Blocks"></param>
        internal static void InitializeSSA(Dictionary<string, VariableAnalysisDetails> VariableAnalysis, Block Entry, List<Block> Blocks)
        {
            VariablesDictionary = new Dictionary<string, VariableAnalysisDetails>(StringComparer.OrdinalIgnoreCase);
            InternalVariablesDictionary.Clear();
            SSADictionary.Clear();
            Counters.Clear();

            DominanceFrontiers(Blocks, Entry);

            InsertPhiNodes(VariableAnalysis);

            foreach (var key in VariableAnalysis.Keys.ToList())
            {
                SSADictionary[key] = new Stack<VariableAnalysisDetails>();
                Counters[key] = 0;
            }

            RenameVariables(Entry);
        }
        
        /// <summary>
        /// Sparse simple constant algorithm using use-def chain from SSA graph.
        /// </summary>
        /// <param name="Variables"></param>
        /// <param name="Entry"></param>
        /// <returns></returns>
        internal static Dictionary<string, VariableAnalysisDetails> SparseSimpleConstants(Dictionary<string, VariableAnalysisDetails> Variables, Block Entry)
        {
            List<Block> blocks = GenerateReverseDepthFirstOrder(Entry);
            InitializeSSA(Variables, Entry, blocks);

            LinkedList<Tuple<Block, Block>> workLists = new LinkedList<Tuple<Block, Block>>();

            foreach (var block in blocks)
            {
                foreach (var ssaSucc in block.SSASuccessors)
                {
                    workLists.AddLast(Tuple.Create(block, ssaSucc));
                }
            }

            // Initialize variables in internaldictionary to undetermined
            foreach (var key in InternalVariablesDictionary.Keys.ToList())
            {
                InternalVariablesDictionary[key].Constant = Undetermined.UndeterminedConstant;
                InternalVariablesDictionary[key].Type = typeof(Undetermined);
            }

            // Initialze DefinedBlock of phi function. If it is null then that phi function is not initialized;
            foreach (var block in blocks)
            {
                foreach (var phi in block.Phis)
                {
                    foreach (var operand in phi.Operands)
                    {
                        phi.DefinedBlock = operand.DefinedBlock;
                        if (phi.DefinedBlock == null)
                        {
                            break;
                        }
                    }

                    InternalVariablesDictionary[phi.Name].DefinedBlock = phi.DefinedBlock;
                }
            }

            while (workLists.Count != 0)
            {
                Tuple<Block, Block> SSAEdge = workLists.Last.Value;
                workLists.RemoveLast();
                Block start = SSAEdge.Item1;
                Block end = SSAEdge.Item2;

                HashSet<string> defVariables = new HashSet<string>();

                foreach (var obj in start._asts)
                {
                    var assigned = obj as AssignmentTarget;
                    if (assigned != null && assigned.Name != null && InternalVariablesDictionary.ContainsKey(assigned.Name))
                    {
                        var varAnalysis = InternalVariablesDictionary[assigned.Name];
                        varAnalysis.Constant = assigned.Constant;
                        varAnalysis.Type = assigned.Type;
                        defVariables.Add(assigned.Name);
                    }
                }

                foreach (var phi in start.Phis)
                {
                    EvaluatePhiConstant(phi);
                    EvaluatePhiType(phi);
                    defVariables.Add(phi.Name);
                }

                bool updated = false;

                List<VariableTarget> varTargets = new List<VariableTarget>();
                varTargets.AddRange(end._asts.Where(item => item is VariableTarget).Cast<VariableTarget>());

                foreach (var phi in end.Phis)
                {
                    varTargets.AddRange(phi.Operands);
                }

                foreach (var varTarget in varTargets)
                {
                    if (defVariables.Contains(varTarget.Name))
                    {
                        var analysisDetails = InternalVariablesDictionary[varTarget.Name];
                        if ((varTarget.Constant == null && varTarget.Constant != analysisDetails.Constant)
                            || (varTarget.Constant != null && !varTarget.Constant.Equals(analysisDetails.Constant))
                            || varTarget.Type != analysisDetails.Type)
                        {
                            updated = true;
                        }

                        varTarget.Constant = analysisDetails.Constant;
                        varTarget.Type = analysisDetails.Type;

                        VariablesDictionary[VariableAnalysis.AnalysisDictionaryKey(varTarget.VarAst)] = InternalVariablesDictionary[varTarget.Name];
                    }
                }

                foreach (var ast in end._asts)
                {
                    var assigned = ast as AssignmentTarget;
                    if (assigned != null && assigned.Name != null && defVariables.Contains(assigned.Name)
                        && assigned._rightHandSideVariable != null && assigned._leftHandSideVariable != null)
                    {
                        // Ignore assignments like $a = $a
                        if (!String.Equals(assigned._rightHandSideVariable.Name, assigned.Name)
                            && InternalVariablesDictionary.ContainsKey(assigned._rightHandSideVariable.Name))
                        {
                            var analysisDetails = InternalVariablesDictionary[assigned._rightHandSideVariable.Name];
                            if ((assigned.Constant == null && assigned.Constant != analysisDetails.Constant)
                                || (assigned.Constant != null && !assigned.Constant.Equals(analysisDetails.Constant))
                                || assigned.Type != analysisDetails.Type)
                            {
                                updated = true;
                            }

                            assigned.Constant = analysisDetails.Constant;
                            assigned.Type = analysisDetails.Type;
                        }
                    }
                }

                if (updated)
                {
                    foreach (var block in end.SSASuccessors)
                    {
                        // Add to the front instead of the end since we are removing from end.
                        workLists.AddFirst(Tuple.Create(end, block));
                    }
                }
            }

            return VariablesDictionary;
        }

        /// <summary>
        /// Evaluate the constant value at a phi node
        /// </summary>
        /// <param name="phi"></param>
        internal static void EvaluatePhiConstant(Phi phi)
        {
            int allUnreached = 0;
            object constantSoFar = null;
            bool first = true;

            if (phi.Operands.Length == 0 || phi.DefinedBlock == null)
            {
                return;
            }

            foreach (var operand in phi.Operands)
            {
                var operandDetails = InternalVariablesDictionary[operand.Name];

                if (operandDetails.Constant == Undetermined.UndeterminedConstant)
                {
                    constantSoFar = Undetermined.UndeterminedConstant;
                    break;
                }
                else
                {
                    if (first)
                    {
                        first = false;
                        constantSoFar = operandDetails.Constant;

                        if (constantSoFar == Unreached.UnreachedConstant)
                        {
                            allUnreached += 1;
                        }
                    }
                    else
                    {
                        if (operandDetails.Constant == Unreached.UnreachedConstant)
                        {
                            allUnreached += 1;
                        }
                        else if ((operandDetails.Constant == null && operandDetails.Constant != constantSoFar)
                            || (operandDetails.Constant != null && !operandDetails.Constant.Equals(constantSoFar)))
                        {
                            constantSoFar = Undetermined.UndeterminedConstant;
                            break;
                        }
                    }
                }

            }

            if (allUnreached == phi.Operands.Length)
            {
                constantSoFar = Unreached.UnreachedConstant;
            }

            InternalVariablesDictionary[phi.Name].Constant = constantSoFar;

            return;
        }

        /// <summary>
        /// Evaluate the type of a phi node.
        /// </summary>
        /// <param name="phi"></param>
        internal static void EvaluatePhiType(Phi phi)
        {
            int allUnreached = 0;
            Type typeSoFar = null;
            bool first = true;

            if (phi.Operands.Length == 0 || phi.DefinedBlock == null)
            {
                return;
            }

            foreach (var operand in phi.Operands)
            {
                var operandDetails = InternalVariablesDictionary[operand.Name];

                if (operandDetails.Type == typeof(Undetermined))
                {
                    typeSoFar = typeof(Undetermined);
                    break;
                }
                else
                {
                    if (first)
                    {
                        first = false;
                        typeSoFar = operandDetails.Type;

                        if (typeSoFar == typeof(Unreached))
                        {
                            allUnreached += 1;
                        }
                    }
                    else
                    {
                        if (operandDetails.Type == typeof(Unreached))
                        {
                            allUnreached += 1;
                        }
                        else if (typeSoFar != operandDetails.Type)
                        {
                            typeSoFar = typeof(Undetermined);
                            break;
                        }
                    }
                }

            }

            if (allUnreached == phi.Operands.Length)
            {
                typeSoFar = typeof(Unreached);
            }

            InternalVariablesDictionary[phi.Name].Type = typeSoFar;

            return;
        }

        internal static List<Block> GenerateReverseDepthFirstOrder(Block block)
        {
            count = 0;
            List<Block> result = new List<Block>();

            VisitDepthFirstOrder(block, result);
            result.Reverse();
         
            for (int i = 0; i < result.Count; i++)
            {
                result[i]._visitData = null;
            }

            return result;
        }

        internal static void VisitDepthFirstOrder(Block block, List<Block> visitData)
        {
            if (ReferenceEquals(block._visitData, visitData))
                return;

            block._visitData = visitData;
            
            foreach (Block succ in block._successors)
            {
                VisitDepthFirstOrder(succ, visitData);
            }

            visitData.Add(block);
            block.PostOrder = count;
            count += 1;
        }
    }

    /// <summary>
    /// Class used to store a variable
    /// </summary>
    public class VariableTarget : VariableDetails
    {
        internal VariableExpressionAst VarAst;

        /// <summary>
        /// Constructor that takes in a VariableExpressionAst
        /// </summary>
        /// <param name="varExpression"></param>
        public VariableTarget(VariableExpressionAst varExpression)
        {
            if (varExpression != null)
            {
                Name = AssignmentTarget.GetUnaliasedVariableName(varExpression.VariablePath);
                VarAst = varExpression;
                RealName = Name;
            }
        }

        /// <summary>
        /// Constructor that takes in a VariableDetails
        /// Used for phi operator
        /// </summary>
        /// <param name="VarDetails"></param>
        public VariableTarget(VariableDetails VarDetails)
        {
            if (VarDetails != null)
            {
                Name = VarDetails.Name;
                RealName = VarDetails.RealName;
                DefinedBlock = VarDetails.DefinedBlock;
            }
        }

        /// <summary>
        /// Empty constructor
        /// </summary>
        public VariableTarget()
        {
        }
    }

    class AssignmentTarget : VariableDetails
    {
        internal readonly ExpressionAst _targetAst;
        internal readonly StatementAst _rightAst;
        internal VariableTarget _rightHandSideVariable;
        internal VariableExpressionAst _leftHandSideVariable;
        internal bool OnlyTypeInitialized = false;

        public AssignmentTarget(ExpressionAst ast)
        {
            this._targetAst = ast;
            SetVariableName();
        }

        public AssignmentTarget(AssignmentStatementAst ast)
        {
            this._targetAst = ast.Left;
            this._rightAst = ast.Right;

            if (_rightAst != null)
            {
                Constant = Undetermined.UndeterminedConstant;
                Type = typeof(Undetermined);
            }

            if (_rightAst is CommandExpressionAst)
            {
                ExpressionAst exprAst = (_rightAst as CommandExpressionAst).Expression;
                Type = exprAst.StaticType;

                if (exprAst is ConvertExpressionAst)
                {
                    ConvertExpressionAst convertAst = exprAst as ConvertExpressionAst;

                    Type = DeepestRelatedDerivedClass(convertAst.StaticType, Type);

                    if (convertAst.Child is ConstantExpressionAst)
                    {
                        Constant = (convertAst.Child as ConstantExpressionAst).Value;
                    }
                }
                else if (exprAst is InvokeMemberExpressionAst)
                {
                    InvokeMemberExpressionAst imeAst = exprAst as InvokeMemberExpressionAst;
                    if (imeAst.Member is StringConstantExpressionAst &&
                        String.Equals((imeAst.Member as StringConstantExpressionAst).Value, "new", StringComparison.OrdinalIgnoreCase)
                        && imeAst.Expression is TypeExpressionAst)
                    {
                        Type = DeepestRelatedDerivedClass(Type, (imeAst.Expression as TypeExpressionAst).TypeName.GetReflectionType());
                    }
                }
                else
                {
                    BinaryExpressionAst binAst = exprAst as BinaryExpressionAst;
                    if (binAst != null && binAst.Operator == TokenKind.As && binAst.Right is TypeExpressionAst)
                    {
                        Type = DeepestRelatedDerivedClass((binAst.Right as TypeExpressionAst).TypeName.GetReflectionType(),
                            binAst.Left.StaticType);

                        if (binAst.Left is ConstantExpressionAst)
                        {
                            Constant = (binAst.Left as ConstantExpressionAst).Value;
                        }
                        else if (binAst.Left is VariableExpressionAst)
                        {
                            _rightHandSideVariable = new VariableTarget(binAst.Left as VariableExpressionAst);
                        }
                    }
                    else if ((_rightAst as CommandExpressionAst).Expression is ConstantExpressionAst)
                    {
                        Constant = ((_rightAst as CommandExpressionAst).Expression as ConstantExpressionAst).Value;
                    }
                    else if ((_rightAst as CommandExpressionAst).Expression is VariableExpressionAst)
                    {
                        _rightHandSideVariable = new VariableTarget((_rightAst as CommandExpressionAst).Expression as VariableExpressionAst);
                    }
                    //Store the type info for variable assignment from .Net type
                    else if ((_rightAst as CommandExpressionAst).Expression is MemberExpressionAst)
                    {
                        MemberExpressionAst memberAst = ((_rightAst as CommandExpressionAst).Expression) as MemberExpressionAst;
                        //syntax like $a=[System.AppDomain]::CurrentDomain
                        if (memberAst.Expression is TypeExpressionAst)
                        {
                            Type = DeepestRelatedDerivedClass(Type, (memberAst.Expression as TypeExpressionAst).TypeName.GetReflectionType());
                        }
                    }
                }
            }
            // We'll consider case where there is only 1 pipeline element for now
            else if (_rightAst is PipelineAst && (_rightAst as PipelineAst).PipelineElements.Count == 1)
            {
                CommandAst cmdAst = (_rightAst as PipelineAst).PipelineElements[0] as CommandAst;

                if (cmdAst != null && cmdAst.CommandElements.Count > 1)
                {
                    StringConstantExpressionAst stringAst = cmdAst.CommandElements[0] as StringConstantExpressionAst;

                    if (stringAst != null && String.Equals(stringAst.Value, "new-object", StringComparison.OrdinalIgnoreCase))
                    {
                        CommandParameterAst secondElement = cmdAst.CommandElements[1] as CommandParameterAst;
                        StringConstantExpressionAst typeName = null;

                        if (secondElement != null)
                        {
                            if (String.Equals(secondElement.ParameterName, "TypeName", StringComparison.OrdinalIgnoreCase))
                            {
                                if (secondElement.Argument != null)
                                {
                                    typeName = secondElement.Argument as StringConstantExpressionAst;
                                }
                                else
                                {
                                    if (cmdAst.CommandElements.Count > 2)
                                    {
                                        typeName = cmdAst.CommandElements[2] as StringConstantExpressionAst;
                                    }
                                }
                            }
                        }
                        else
                        {
                            typeName = cmdAst.CommandElements[1] as StringConstantExpressionAst;
                        }

                        if (typeName != null)
                        {
                            Type = System.Type.GetType(typeName.Value) ?? typeof(object);
                        }
                    }
                }
            }

            SetVariableName();
        }

        private void SetVariableName()
        {
            ExpressionAst lhs = (_targetAst is ConvertExpressionAst) ? (_targetAst as ConvertExpressionAst).Child : _targetAst;

            _leftHandSideVariable = lhs as VariableExpressionAst;

            if (_leftHandSideVariable == null)
            {
                return;
            }

            Name = GetUnaliasedVariableName(_leftHandSideVariable.VariablePath);
            RealName = Name;
        }

        public AssignmentTarget(string variableName, Type type)
        {
            this.Name = variableName;
            this.Type = type;
        }

        /// <summary>
        /// Given 2 types, return the type that is the subclass of both.
        /// </summary>
        /// <param name="FirstType"></param>
        /// <param name="SecondType"></param>
        /// <returns></returns>
        public static Type DeepestRelatedDerivedClass(Type FirstType, Type SecondType)
        {
            if (FirstType == null || SecondType == null)
            {
                return typeof(object);
            }

            if (FirstType.IsSubclassOf(SecondType) || FirstType == SecondType)
            {
                return FirstType;
            }
            else if (SecondType.IsSubclassOf(FirstType))
            {
                return SecondType;
            }

            return typeof(object);
        }

        internal static string GetUnaliasedVariableName(string varName)
        {
            return varName.Equals(SpecialVars.PSItem, StringComparison.OrdinalIgnoreCase)
                       ? SpecialVars.Underbar
                       : varName;
        }

        [Flags]
        internal enum VariablePathFlags
        {
            None = 0x00,
            Local = 0x01,
            Script = 0x02,
            Global = 0x04,
            Private = 0x08,
            Variable = 0x10,
            Function = 0x20,
            DriveQualified = 0x40,
            Unqualified = 0x80,

            // If any of these bits are set, the path does not represent an unscoped variable.          
            UnscopedVariableMask = Local | Script | Global | Private | Function | DriveQualified,
        }

        internal static string GetUnaliasedVariableName(VariablePath varPath)
        {
            VariablePathFlags knownFlags = VariablePathFlags.None;
            string path = varPath.ToString();
            int currentCharIndex = 0;
            int lastScannedColon = -1;
            string candidateScope = null;
            string candidateScopeUpper = null;
            string unqualifiedPath;
            VariablePathFlags _flags = VariablePathFlags.None;
            VariablePathFlags candidateFlags = VariablePathFlags.Unqualified;

            if (varPath.IsDriveQualified)
            {
                knownFlags = VariablePathFlags.DriveQualified;
            }

        scanScope:
            switch (path[0])
            {
                case 'g':
                case 'G':
                    candidateScope = "lobal";
                    candidateScopeUpper = "LOBAL";
                    candidateFlags = VariablePathFlags.Global;
                    break;
                case 'l':
                case 'L':
                    candidateScope = "ocal";
                    candidateScopeUpper = "OCAL";
                    candidateFlags = VariablePathFlags.Local;
                    break;
                case 'p':
                case 'P':
                    candidateScope = "rivate";
                    candidateScopeUpper = "RIVATE";
                    candidateFlags = VariablePathFlags.Private;
                    break;
                case 's':
                case 'S':
                    candidateScope = "cript";
                    candidateScopeUpper = "CRIPT";
                    candidateFlags = VariablePathFlags.Script;
                    break;
                case 'v':
                case 'V':
                    if (knownFlags == VariablePathFlags.None)
                    {
                        // If we see 'variable:', our namespaceId will be empty, and                      
                        // we'll also need to scan for the scope again.                      
                        candidateScope = "ariable";
                        candidateScopeUpper = "ARIABLE";
                        candidateFlags = VariablePathFlags.Variable;
                    }
                    break;
            }

            if (candidateScope != null)
            {
                currentCharIndex += 1; // First character already matched.                  
                int j;
                for (j = 0; currentCharIndex < path.Length && j < candidateScope.Length; ++j, ++currentCharIndex)
                {
                    if (path[currentCharIndex] != candidateScope[j] && path[currentCharIndex] != candidateScopeUpper[j])
                    {
                        break;
                    }
                }

                if (j == candidateScope.Length &&
                    currentCharIndex < path.Length &&
                    path[currentCharIndex] == ':')
                {
                    if (_flags == VariablePathFlags.None)
                    {
                        _flags = VariablePathFlags.Variable;
                    }
                    _flags |= candidateFlags;
                    lastScannedColon = currentCharIndex;
                    currentCharIndex += 1;

                    // If saw 'variable:', we need to look for a scope after 'variable:'.                      
                    if (candidateFlags == VariablePathFlags.Variable)
                    {
                        knownFlags = VariablePathFlags.Variable;
                        candidateScope = candidateScopeUpper = null;
                        candidateFlags = VariablePathFlags.None;
                        goto scanScope;
                    }
                }
            }

            if (_flags == VariablePathFlags.None)
            {
                lastScannedColon = path.IndexOf(':', currentCharIndex);
                // No colon, or a colon as the first character means we have                  
                // a simple variable, otherwise it's a drive.                  
                if (lastScannedColon > 0)
                {
                    _flags = VariablePathFlags.DriveQualified;
                }
            }

            if (lastScannedColon == -1)
            {
                unqualifiedPath = path;
            }
            else
            {
                unqualifiedPath = path.Substring(lastScannedColon + 1);
            }

            if (_flags == VariablePathFlags.None)
            {
                _flags = VariablePathFlags.Unqualified | VariablePathFlags.Variable;
            }

            return GetUnaliasedVariableName(unqualifiedPath);
        }
    }

    /// <summary>
    /// <see cref="IFlowGraph"/> interface
    /// </summary>
    public interface IFlowGraph : ICustomAstVisitor
    {
        /// <summary/>
        void Init();

        /// <summary/>
        void Start();

        /// <summary/>
        void Stop();

        /// <summary/>
        Block Current { get; set; }

        /// <summary/>
        Block Entry { get; set; }

        /// <summary/>
        Block Exit { get; set; }

        /// <summary/>
        IFlowGraph Decorator { get; set; }
    }

    /// <summary>
    /// Data flow graph
    /// </summary>
    public class FlowGraph : IFlowGraph
    {
        internal Block _entryBlock;
        internal Block _exitBlock;
        internal Block _currentBlock;
        internal readonly List<LoopGotoTargets> _loopTargets = new List<LoopGotoTargets>();

        /// <summary>
        /// Start the data flow graph.
        /// </summary>
        public void Start()
        {
            Init();
            _currentBlock = _entryBlock;
        }

        /// <summary>
        /// Stop the data flow graph.
        /// </summary>
        public void Stop()
        {
            _currentBlock.FlowsTo(_exitBlock);
        }

        /// <summary>
        /// The current block
        /// </summary>
        public Block Current
        {
            get
            {
                return _currentBlock;
            }
            set
            {
                _currentBlock = value;
            }
        }

        /// <summary>
        /// The entry to the data flow
        /// </summary>
        public Block Entry
        {
            get
            {
                return _entryBlock;
            }
            set
            {
                _entryBlock = value;
            }
        }

        /// <summary>
        /// The exit of the data flow
        /// </summary>
        public Block Exit
        {
            get
            {
                return _exitBlock;
            }
            set
            {
                _exitBlock = value;
            }
        }

        /// <summary>
        /// Initialize the data flow construction
        /// </summary>
        public void Init()
        {
            _entryBlock = new Block();
            _exitBlock = new Block();
        }

        /// <summary>
        /// The decorator
        /// </summary>
        public IFlowGraph Decorator { get; set; }

        /// <summary>
        /// Visit error statement
        /// </summary>
        /// <param name="errorStatementAst"></param>
        /// <returns></returns>
        public object VisitErrorStatement(ErrorStatementAst errorStatementAst)
        {
            return null;
        }

        /// <summary>
        /// Visit error expression
        /// </summary>
        /// <param name="errorExpressionAst"></param>
        /// <returns></returns>
        public object VisitErrorExpression(ErrorExpressionAst errorExpressionAst)
        {
            return null;
        }

        /// <summary>
        /// Visit script block
        /// </summary>
        /// <param name="scriptBlockAst"></param>
        /// <returns></returns>
        public object VisitScriptBlock(ScriptBlockAst scriptBlockAst)
        {
            if (scriptBlockAst == null) return null;

            _currentBlock = _entryBlock;

            if (scriptBlockAst.DynamicParamBlock != null)
            {
                scriptBlockAst.DynamicParamBlock.Visit(this.Decorator);
            }

            if (scriptBlockAst.BeginBlock != null)
            {
                scriptBlockAst.BeginBlock.Visit(this.Decorator);
            }
            if (scriptBlockAst.ProcessBlock != null)
            {
                scriptBlockAst.ProcessBlock.Visit(this.Decorator);
            }
            if (scriptBlockAst.EndBlock != null)
            {
                scriptBlockAst.EndBlock.Visit(this.Decorator);
            }

            _currentBlock.FlowsTo(_exitBlock);

            return null;
        }

        /// <summary>
        /// Visit param block
        /// </summary>
        /// <param name="paramBlockAst"></param>
        /// <returns></returns>
        public object VisitParamBlock(ParamBlockAst paramBlockAst)
        {
            return null;
        }

        /// <summary>
        /// Visit named block
        /// </summary>
        /// <param name="namedBlockAst"></param>
        /// <returns></returns>
        public object VisitNamedBlock(NamedBlockAst namedBlockAst)
        {
            if (namedBlockAst == null) return null;
            // Don't visit traps - they get their own scope
            return VisitStatementBlock(namedBlockAst.Statements);
        }

        /// <summary>
        /// Visit type constraint
        /// </summary>
        /// <param name="typeConstraintAst"></param>
        /// <returns></returns>
        public object VisitTypeConstraint(TypeConstraintAst typeConstraintAst)
        {
            System.Diagnostics.Debug.Assert(false, "Code is unreachable");
            return null;
        }

        /// <summary>
        /// Visit attribute
        /// </summary>
        /// <param name="attributeAst"></param>
        /// <returns></returns>
        public object VisitAttribute(AttributeAst attributeAst)
        {
            System.Diagnostics.Debug.Assert(false, "Code is unreachable");
            return null;
        }

        /// <summary>
        /// Visit named attribute
        /// </summary>
        /// <param name="namedAttributeArgumentAst"></param>
        /// <returns></returns>
        public object VisitNamedAttributeArgument(NamedAttributeArgumentAst namedAttributeArgumentAst)
        {
            System.Diagnostics.Debug.Assert(false, "Code is unreachable");
            return null;
        }

        /// <summary>
        /// Visit parameter
        /// </summary>
        /// <param name="parameterAst"></param>
        /// <returns></returns>
        public object VisitParameter(ParameterAst parameterAst)
        {
            // Nothing to do now, we've already allocated parameters in the first pass looking for all variable naems.
            System.Diagnostics.Debug.Assert(false, "Code is unreachable");
            return null;
        }

        /// <summary>
        /// Visit function
        /// </summary>
        /// <param name="functionDefinitionAst"></param>
        /// <returns></returns>
        public object VisitFunctionDefinition(FunctionDefinitionAst functionDefinitionAst)
        {
            // Don't recurse into the function definition, it's variables are distinct from the script block
            // we're currently analyzing.

            return null;
        }

        /// <summary>
        /// Visit statementblock
        /// </summary>
        /// <param name="statementBlockAst"></param>
        /// <returns></returns>
        public object VisitStatementBlock(StatementBlockAst statementBlockAst)
        {
            if (statementBlockAst == null) return null;
            // Don't visit traps - they get their own scope
            return VisitStatementBlock(statementBlockAst.Statements);
        }

        /// <summary>
        /// Visit list of statement
        /// </summary>
        /// <param name="statements"></param>
        /// <returns></returns>
        object VisitStatementBlock(ReadOnlyCollection<StatementAst> statements)
        {
            foreach (var stmt in statements)
            {
                stmt.Visit(this.Decorator);
            }

            return null;
        }

        /// <summary>
        /// Visit if statement
        /// </summary>
        /// <param name="ifStmtAst"></param>
        /// <returns></returns>
        public object VisitIfStatement(IfStatementAst ifStmtAst)
        {
            if (ifStmtAst == null) return null;

            Block afterStmt = new Block();
            int clauseCount = ifStmtAst.Clauses.Count;
            for (int i = 0; i < clauseCount; i++)
            {
                var clause = ifStmtAst.Clauses[i];
                bool isLastClause = (i == (clauseCount - 1) && ifStmtAst.ElseClause == null);
                Block clauseBlock = new Block();
                Block nextBlock = isLastClause ? afterStmt : new Block();

                clause.Item1.Visit(this);

                _currentBlock.FlowsTo(clauseBlock);
                _currentBlock.FlowsTo(nextBlock);
                _currentBlock = clauseBlock;

                clause.Item2.Visit(this);

                _currentBlock.FlowsTo(afterStmt);
                _currentBlock = nextBlock;
            }

            if (ifStmtAst.ElseClause != null)
            {
                ifStmtAst.ElseClause.Visit(this);
                _currentBlock.FlowsTo(afterStmt);
            }

            _currentBlock = afterStmt;
            return null;
        }

        /// <summary>
        /// Visit trap
        /// </summary>
        /// <param name="trapStatementAst"></param>
        /// <returns></returns>
        public object VisitTrap(TrapStatementAst trapStatementAst)
        {
            if (trapStatementAst == null) return null;

            trapStatementAst.Body.Visit(this.Decorator);
            return null;
        }

        /// <summary>
        /// Visit switch statement
        /// </summary>
        /// <param name="switchStatementAst"></param>
        /// <returns></returns>
        public object VisitSwitchStatement(SwitchStatementAst switchStatementAst)
        {
            if (switchStatementAst == null) return null;

            Action generateCondition = () =>
            {
                switchStatementAst.Condition.Visit(this.Decorator);

                // $switch is set after evaluating the condition.
                _currentBlock.AddAst(new AssignmentTarget(SpecialVars.@switch, typeof(IEnumerator)));
            };

            Action switchBodyGenerator = () =>
            {
                bool hasDefault = (switchStatementAst.Default != null);
                Block afterStmt = new Block();

                int clauseCount = switchStatementAst.Clauses.Count;
                for (int i = 0; i < clauseCount; i++)
                {
                    var clause = switchStatementAst.Clauses[i];
                    Block clauseBlock = new Block();
                    bool isLastClause = (i == (clauseCount - 1) && !hasDefault);
                    Block nextBlock = isLastClause ? afterStmt : new Block();

                    clause.Item1.Visit(this.Decorator);

                    _currentBlock.FlowsTo(nextBlock);
                    _currentBlock.FlowsTo(clauseBlock);
                    _currentBlock = clauseBlock;

                    clause.Item2.Visit(this.Decorator);

                    if (!isLastClause)
                    {
                        _currentBlock.FlowsTo(nextBlock);
                        _currentBlock = nextBlock;
                    }
                }

                if (hasDefault)
                {
                    // If any clause was executed, we skip the default, so there is always a branch over the default.
                    _currentBlock.FlowsTo(afterStmt);
                    switchStatementAst.Default.Visit(this.Decorator);
                }

                _currentBlock.FlowsTo(afterStmt);
                _currentBlock = afterStmt;
            };

            GenerateWhileLoop(switchStatementAst.Label, generateCondition, switchBodyGenerator);

            return null;
        }

        /// <summary>
        /// Visit data statement
        /// </summary>
        /// <param name="dataStatementAst"></param>
        /// <returns></returns>
        public object VisitDataStatement(DataStatementAst dataStatementAst)
        {
            if (dataStatementAst == null) return null;

            dataStatementAst.Body.Visit(this.Decorator);
            if (dataStatementAst.Variable != null)
            {
                _currentBlock.AddAst(dataStatementAst);
            }
            return null;
        }

        internal void GenerateWhileLoop(string loopLabel,
                                       Action generateCondition,
                                       Action generateLoopBody,
                                       Ast continueAction = null)
        {
            // We model the flow graph like this (if continueAction is null, the first part is slightly different):
            //    goto L
            //    :ContinueTarget
            //        continueAction
            //    :L
            //    if (condition)
            //    {
            //        loop body
            //        // break -> goto BreakTarget
            //        // continue -> goto ContinueTarget
            //        goto ContinueTarget
            //    }
            //    :BreakTarget

            var continueBlock = new Block();

            if (continueAction != null)
            {
                var blockAfterContinue = new Block();

                // Represent the goto over the condition before the first iteration.
                _currentBlock.FlowsTo(blockAfterContinue);

                _currentBlock = continueBlock;
                continueAction.Visit(this.Decorator);

                _currentBlock.FlowsTo(blockAfterContinue);
                _currentBlock = blockAfterContinue;
            }
            else
            {
                _currentBlock.FlowsTo(continueBlock);
                _currentBlock = continueBlock;
            }

            var bodyBlock = new Block();
            var breakBlock = new Block();

            // Condition can be null from an uncommon for loop: for() {}
            if (generateCondition != null)
            {
                generateCondition();
                _currentBlock.FlowsTo(breakBlock);
            }

            _loopTargets.Add(new LoopGotoTargets(loopLabel ?? "", breakBlock, continueBlock));
            _currentBlock.FlowsTo(bodyBlock);
            _currentBlock = bodyBlock;
            generateLoopBody();
            _currentBlock.FlowsTo(continueBlock);

            _currentBlock = breakBlock;

            _loopTargets.RemoveAt(_loopTargets.Count - 1);
        }

        internal void GenerateDoLoop(LoopStatementAst loopStatement)
        {
            // We model the flow graph like this:
            //    :RepeatTarget
            //       loop body
            //       // break -> goto BreakTarget
            //       // continue -> goto ContinueTarget
            //    :ContinueTarget
            //    if (condition)
            //    {
            //        goto RepeatTarget
            //    }
            //    :BreakTarget

            var continueBlock = new Block();
            var bodyBlock = new Block();
            var breakBlock = new Block();
            var gotoRepeatTargetBlock = new Block();

            _loopTargets.Add(new LoopGotoTargets(loopStatement.Label ?? "", breakBlock, continueBlock));

            _currentBlock.FlowsTo(bodyBlock);
            _currentBlock = bodyBlock;

            loopStatement.Body.Visit(this.Decorator);

            _currentBlock.FlowsTo(continueBlock);
            _currentBlock = continueBlock;

            loopStatement.Condition.Visit(this.Decorator);

            _currentBlock.FlowsTo(breakBlock);
            _currentBlock.FlowsTo(gotoRepeatTargetBlock);

            _currentBlock = gotoRepeatTargetBlock;
            _currentBlock.FlowsTo(bodyBlock);

            _currentBlock = breakBlock;

            _loopTargets.RemoveAt(_loopTargets.Count - 1);
        }

        /// <summary>
        /// Visit foreach statement
        /// </summary>
        /// <param name="forEachStatementAst"></param>
        /// <returns></returns>
        public object VisitForEachStatement(ForEachStatementAst forEachStatementAst)
        {
            if (forEachStatementAst == null) return null;

            var afterFor = new Block();
            Action generateCondition = () =>
            {
                forEachStatementAst.Condition.Visit(this.Decorator);

                // The loop might not be executed, so add flow around the loop.
                _currentBlock.FlowsTo(afterFor);

                // $foreach and the iterator variable are set after evaluating the condition.
                _currentBlock.AddAst(new AssignmentTarget(SpecialVars.@foreach, typeof(IEnumerator)));
                _currentBlock.AddAst(new AssignmentTarget(forEachStatementAst.Variable));
            };

            GenerateWhileLoop(forEachStatementAst.Label, generateCondition, () => forEachStatementAst.Body.Visit(this.Decorator));

            _currentBlock.FlowsTo(afterFor);
            _currentBlock = afterFor;

            return null;
        }

        /// <summary>
        /// Visit do while statement
        /// </summary>
        /// <param name="doWhileStatementAst"></param>
        /// <returns></returns>
        public object VisitDoWhileStatement(DoWhileStatementAst doWhileStatementAst)
        {
            GenerateDoLoop(doWhileStatementAst);
            return null;
        }

        /// <summary>
        /// Visit do until statement
        /// </summary>
        /// <param name="doUntilStatementAst"></param>
        /// <returns></returns>
        public object VisitDoUntilStatement(DoUntilStatementAst doUntilStatementAst)
        {
            GenerateDoLoop(doUntilStatementAst);
            return null;
        }

        /// <summary>
        /// Visit for statement
        /// </summary>
        /// <param name="forStatementAst"></param>
        /// <returns></returns>
        public object VisitForStatement(ForStatementAst forStatementAst)
        {
            if (forStatementAst == null) return null;

            if (forStatementAst.Initializer != null)
            {
                forStatementAst.Initializer.Visit(this.Decorator);
            }

            var generateCondition = forStatementAst.Condition != null
                ? () => forStatementAst.Condition.Visit(this.Decorator)
                : (Action)null;

            GenerateWhileLoop(forStatementAst.Label, generateCondition, () => forStatementAst.Body.Visit(this.Decorator),
                              forStatementAst.Iterator);
            return null;
        }

        /// <summary>
        /// Visit while statement
        /// </summary>
        /// <param name="whileStatementAst"></param>
        /// <returns></returns>
        public object VisitWhileStatement(WhileStatementAst whileStatementAst)
        {
            if (whileStatementAst == null) return null;

            GenerateWhileLoop(whileStatementAst.Label,
                              () => whileStatementAst.Condition.Visit(this.Decorator),
                              () => whileStatementAst.Body.Visit(this.Decorator));
            return null;
        }

        /// <summary>
        /// Visit catch clause
        /// </summary>
        /// <param name="catchClauseAst"></param>
        /// <returns></returns>
        public object VisitCatchClause(CatchClauseAst catchClauseAst)
        {
            if (catchClauseAst == null) return null;

            catchClauseAst.Body.Visit(this.Decorator);
            return null;
        }

        /// <summary>
        /// Visit try statement
        /// </summary>
        /// <param name="tryStatementAst"></param>
        /// <returns></returns>
        public object VisitTryStatement(TryStatementAst tryStatementAst)
        {
            if (tryStatementAst == null) return null;

            // We don't attempt to accurately model flow in a try catch because every statement
            // can flow to each catch.  Instead, we'll assume the try block is not executed (because the very first statement
            // may throw), and have the data flow assume the block before the try is all that can reach the catches and finally.

            var blockBeforeTry = _currentBlock;
            _currentBlock = new Block();
            blockBeforeTry.FlowsTo(_currentBlock);

            tryStatementAst.Body.Visit(this.Decorator);

            // This is either the first block in the finally, or the first block after all the catches if there is no finally.
            // For return analysis, we start off assuming this block is reachable only if the end of the try body
            // is reachable, but that can change if we find a catch that falls through.
            var afterTry = new Block { _unreachable = _currentBlock._unreachable };

            foreach (var catchAst in tryStatementAst.CatchClauses)
            {
                // Any statement in the try block could throw and reach the catch, so assume the worst (from a data
                // flow perspective) and make the predecessor to the catch the block before entering the try.
                _currentBlock = new Block();
                blockBeforeTry.FlowsTo(_currentBlock);

                catchAst.Visit(this.Decorator);

                if (!_currentBlock._unreachable)
                {
                    // The last block of the catch falls through, so we can get reach blocks after the try.
                    afterTry._unreachable = false;
                }
            }

            // Assume nothing in the try was executed, so flow comes from before the try.  We could have the catch blocks
            // also flow to this block, but that won't improve the analysis any, so skip that.
            _currentBlock = afterTry;
            blockBeforeTry.FlowsTo(_currentBlock);

            if (tryStatementAst.Finally != null)
            {
                tryStatementAst.Finally.Visit(this.Decorator);

                if (!_currentBlock._unreachable)
                {
                    // If the finally throws (it can't have other flow like return, break, or continue)
                    // then after the try/catch/finally is unreachable, otherwise it is reachable.
                    afterTry._unreachable = false;
                }
                // We can assume that flow from the finally reaches the code following the finally.  Of course, if an exception occurs,
                // then the code can't be reached, but our conservative data flow modeling assures us correctness, the exception will
                // either leave the current function (meaning it doesn't matter that we assumed flow reached after the finally), or
                // it will be caught and handled by a containing catch, and our conversative data flow ensures the correctness of the
                // generated code.

                var newBlock = new Block();
                _currentBlock.FlowsTo(newBlock);
                _currentBlock = newBlock;
            }

            return null;
        }

        void BreakOrContinue(ExpressionAst label, Func<LoopGotoTargets, Block> fieldSelector)
        {
            Block targetBlock = null;
            if (label != null)
            {
                label.Visit(this.Decorator);
                if (_loopTargets.Any())
                {
                    var labelStrAst = label as StringConstantExpressionAst;
                    if (labelStrAst != null)
                    {
                        targetBlock = (from t in _loopTargets
                                       where t.Label.Equals(labelStrAst.Value, StringComparison.OrdinalIgnoreCase)
                                       select fieldSelector(t)).LastOrDefault();
                    }
                }
            }
            else if (_loopTargets.Count > 0)
            {
                targetBlock = fieldSelector(_loopTargets.Last());
            }

            if (targetBlock == null)
            {
                _currentBlock.FlowsTo(_exitBlock);
                _currentBlock._throws = true;
            }
            else
            {
                _currentBlock.FlowsTo(targetBlock);
            }

            // The next block is unreachable, but is necessary to keep the flow graph correct.
            _currentBlock = new Block { _unreachable = true };
        }

        /// <summary>
        /// Visit break statement
        /// </summary>
        /// <param name="breakStatementAst"></param>
        /// <returns></returns>
        public object VisitBreakStatement(BreakStatementAst breakStatementAst)
        {
            if (breakStatementAst == null) return null;

            BreakOrContinue(breakStatementAst.Label, t => t.BreakTarget);
            return null;
        }

        /// <summary>
        /// Visit continue statement
        /// </summary>
        /// <param name="continueStatementAst"></param>
        /// <returns></returns>
        public object VisitContinueStatement(ContinueStatementAst continueStatementAst)
        {
            if (continueStatementAst == null) return null;

            BreakOrContinue(continueStatementAst.Label, t => t.ContinueTarget);
            return null;
        }

        internal Block ControlFlowStatement(PipelineBaseAst pipelineAst)
        {
            if (pipelineAst != null)
            {
                pipelineAst.Visit(this.Decorator);
            }
            _currentBlock.FlowsTo(_exitBlock);
            var lastBlockInStatement = _currentBlock;

            // The next block is unreachable, but is necessary to keep the flow graph correct.
            _currentBlock = new Block { _unreachable = true };
            return lastBlockInStatement;
        }

        /// <summary>
        /// Visit return statement
        /// </summary>
        /// <param name="returnStatementAst"></param>
        /// <returns></returns>
        public object VisitReturnStatement(ReturnStatementAst returnStatementAst)
        {
            if (returnStatementAst == null) return null;

            ControlFlowStatement(returnStatementAst.Pipeline)._returns = true;
            return null;
        }

        /// <summary>
        /// Visit exit statement
        /// </summary>
        /// <param name="exitStatementAst"></param>
        /// <returns></returns>
        public object VisitExitStatement(ExitStatementAst exitStatementAst)
        {
            if (exitStatementAst == null) return null;

            ControlFlowStatement(exitStatementAst.Pipeline)._throws = true;
            return null;
        }

        /// <summary>
        /// Visit throw statement
        /// </summary>
        /// <param name="throwStatementAst"></param>
        /// <returns></returns>
        public object VisitThrowStatement(ThrowStatementAst throwStatementAst)
        {
            if (throwStatementAst == null) return null;

            ControlFlowStatement(throwStatementAst.Pipeline)._throws = true;
            return null;
        }

        /// <summary>
        /// Visit assignmentstatement
        /// </summary>
        /// <param name="assignmentStatementAst"></param>
        /// <returns></returns>
        public object VisitAssignmentStatement(AssignmentStatementAst assignmentStatementAst)
        {
            if (assignmentStatementAst == null) return null;
            assignmentStatementAst.Right.Visit(this.Decorator);
            return null;
        }

        /// <summary>
        /// Visit pipeline
        /// </summary>
        /// <param name="pipelineAst"></param>
        /// <returns></returns>
        public object VisitPipeline(PipelineAst pipelineAst)
        {
            if (pipelineAst == null) return null;

            bool invokesCommand = false;
            foreach (var command in pipelineAst.PipelineElements)
            {
                command.Visit(this.Decorator);
                if (command is CommandAst)
                {
                    invokesCommand = true;
                }
                foreach (var redir in command.Redirections)
                {
                    redir.Visit(this.Decorator);
                }
            }

            // Because non-local gotos are supported, we must model them in the flow graph.  We can't detect them
            // in general, so we must be pessimistic and assume any command invocation could result in non-local
            // break or continue, so add the appropriate edges to our graph.  These edges occur after visiting
            // the command elements because command arguments could create new blocks, and we won't have executed
            // the command yet.
            if (invokesCommand && _loopTargets.Any())
            {
                foreach (var loopTarget in _loopTargets)
                {
                    _currentBlock.FlowsTo(loopTarget.BreakTarget);
                    _currentBlock.FlowsTo(loopTarget.ContinueTarget);
                }

                // The rest of the block is potentially unreachable, so split the current block.
                var newBlock = new Block();
                _currentBlock.FlowsTo(newBlock);
                _currentBlock = newBlock;
            }

            return null;
        }

        /// <summary>
        /// Visit command
        /// </summary>
        /// <param name="commandAst"></param>
        /// <returns></returns>
        public object VisitCommand(CommandAst commandAst)
        {
            if (commandAst == null) return null;

            //Add the check for Tee-Object -Variable var 
            if(string.Equals(commandAst.GetCommandName(),"Tee-Object",StringComparison.OrdinalIgnoreCase))
            {
                foreach (CommandElementAst ceAst in commandAst.CommandElements)
                {
                    if (ceAst is CommandParameterAst)
                    {
                        string paramName = (ceAst as CommandParameterAst).ParameterName;
                        if (string.Equals(paramName, "Variable", StringComparison.OrdinalIgnoreCase))
                        {
                            int index = commandAst.CommandElements.IndexOf(ceAst);
                            if(commandAst.CommandElements.Count > (index+1))
                            {
                                CommandElementAst paramConstant = commandAst.CommandElements[index + 1];
                                if (paramConstant is StringConstantExpressionAst)
                                {
                                    //If common parameters are used, create a variable target and store the variable value
                                    _currentBlock.AddAst(new AssignmentTarget((paramConstant as StringConstantExpressionAst).Value, typeof(string)));
                                }
                            }            
                        }
                    }
                }
            }
            else
            {
                foreach (CommandElementAst ceAst in commandAst.CommandElements)
                {
                    if (ceAst is CommandParameterAst)
                    {
                        string paramName = (ceAst as CommandParameterAst).ParameterName;
                        if (string.Equals(paramName, "ErrorVariable", StringComparison.OrdinalIgnoreCase)
                             || string.Equals(paramName, "WarningVariable", StringComparison.OrdinalIgnoreCase)
                            || string.Equals(paramName, "PipelineVariable ", StringComparison.OrdinalIgnoreCase)
                            || string.Equals(paramName, "OutVariable", StringComparison.OrdinalIgnoreCase))
                        {
                            int index = commandAst.CommandElements.IndexOf(ceAst);
                            if (commandAst.CommandElements.Count > (index + 1))
                            {
                                CommandElementAst paramConstant = commandAst.CommandElements[index + 1];
                                if (paramConstant is StringConstantExpressionAst)
                                {
                                    //If common parameters are used, create a variable target and store the variable value
                                    _currentBlock.AddAst(new AssignmentTarget((paramConstant as StringConstantExpressionAst).Value, typeof(string)));
                                }
                            }
                        }
                    }
                    ceAst.Visit(this.Decorator);
                }
            }
            return null;
        }

        /// <summary>
        /// Visit command expression
        /// </summary>
        /// <param name="commandExpressionAst"></param>
        /// <returns></returns>
        public object VisitCommandExpression(CommandExpressionAst commandExpressionAst)
        {
            if (commandExpressionAst == null) return null;

            commandExpressionAst.Expression.Visit(this.Decorator);
            return null;
        }

        /// <summary>
        /// Visit command parameter
        /// </summary>
        /// <param name="commandParameterAst"></param>
        /// <returns></returns>
        public object VisitCommandParameter(CommandParameterAst commandParameterAst)
        {
            if (commandParameterAst == null) return null;

            if (commandParameterAst.Argument != null)
            {
                commandParameterAst.Argument.Visit(this.Decorator);
            }
            return null;
        }

        /// <summary>
        /// Visit file direction
        /// </summary>
        /// <param name="fileRedirectionAst"></param>
        /// <returns></returns>
        public object VisitFileRedirection(FileRedirectionAst fileRedirectionAst)
        {
            if (fileRedirectionAst == null) return null;

            fileRedirectionAst.Location.Visit(this.Decorator);
            return null;
        }

        /// <summary>
        /// Visit merging redirection
        /// </summary>
        /// <param name="mergingRedirectionAst"></param>
        /// <returns></returns>
        public object VisitMergingRedirection(MergingRedirectionAst mergingRedirectionAst)
        {
            return null;
        }

        /// <summary>
        /// Visit binary expression
        /// </summary>
        /// <param name="binaryExpressionAst"></param>
        /// <returns></returns>
        public object VisitBinaryExpression(BinaryExpressionAst binaryExpressionAst)
        {
            if (binaryExpressionAst == null) return null;

            if (binaryExpressionAst.Operator == TokenKind.And || binaryExpressionAst.Operator == TokenKind.Or)
            {
                // Logical and/or are short circuit operators, so we need to simulate the control flow.  The
                // left operand is always evaluated, visit it's expression in the current block.
                binaryExpressionAst.Left.Visit(this.Decorator);

                // The right operand is condtionally evaluated.  We aren't generating any code here, just
                // modeling the flow graph, so we just visit the right operand in a new block, and have
                // both the current and new blocks both flow to a post-expression block.
                var targetBlock = new Block();
                var nextBlock = new Block();
                _currentBlock.FlowsTo(targetBlock);
                _currentBlock.FlowsTo(nextBlock);
                _currentBlock = nextBlock;

                binaryExpressionAst.Right.Visit(this.Decorator);

                _currentBlock.FlowsTo(targetBlock);
                _currentBlock = targetBlock;
            }
            else
            {
                binaryExpressionAst.Left.Visit(this.Decorator);
                binaryExpressionAst.Right.Visit(this.Decorator);
            }

            return null;
        }

        /// <summary>
        /// Visit unary expression
        /// </summary>
        /// <param name="unaryExpressionAst"></param>
        /// <returns></returns>
        public object VisitUnaryExpression(UnaryExpressionAst unaryExpressionAst)
        {
            if (unaryExpressionAst == null) return null;

            unaryExpressionAst.Child.Visit(this.Decorator);
            return null;
        }

        /// <summary>
        /// Visit convert expression
        /// </summary>
        /// <param name="convertExpressionAst"></param>
        /// <returns></returns>
        public object VisitConvertExpression(ConvertExpressionAst convertExpressionAst)
        {
            if (convertExpressionAst == null) return null;

            convertExpressionAst.Child.Visit(this.Decorator);
            return null;
        }

        /// <summary>
        /// Visit constant expression
        /// </summary>
        /// <param name="constantExpressionAst"></param>
        /// <returns></returns>
        public object VisitConstantExpression(ConstantExpressionAst constantExpressionAst)
        {
            return null;
        }

        /// <summary>
        /// Visit string constant expression
        /// </summary>
        /// <param name="stringConstantExpressionAst"></param>
        /// <returns></returns>
        public object VisitStringConstantExpression(StringConstantExpressionAst stringConstantExpressionAst)
        {
            return null;
        }

        /// <summary>
        /// Visit sub expression
        /// </summary>
        /// <param name="subExpressionAst"></param>
        /// <returns></returns>
        public object VisitSubExpression(SubExpressionAst subExpressionAst)
        {
            if (subExpressionAst == null) return null;

            subExpressionAst.SubExpression.Visit(this.Decorator);
            return null;
        }

        /// <summary>
        /// Visit using expression
        /// </summary>
        /// <param name="usingExpressionAst"></param>
        /// <returns></returns>
        public object VisitUsingExpression(UsingExpressionAst usingExpressionAst)
        {
            // The SubExpression is not visited, we treat this expression like it is a constant that is replaced
            // before the script block is executed
            return null;
        }

        /// <summary>
        /// Visit variable expression
        /// </summary>
        /// <param name="variableExpressionAst"></param>
        /// <returns></returns>
        public object VisitVariableExpression(VariableExpressionAst variableExpressionAst)
        {
            return null;
        }

        /// <summary>
        /// Visit type expression
        /// </summary>
        /// <param name="typeExpressionAst"></param>
        /// <returns></returns>
        public object VisitTypeExpression(TypeExpressionAst typeExpressionAst)
        {
            return null;
        }

        /// <summary>
        /// Visit Member Expression
        /// </summary>
        /// <param name="memberExpressionAst"></param>
        /// <returns></returns>
        public object VisitMemberExpression(MemberExpressionAst memberExpressionAst)
        {
            if (memberExpressionAst == null) return null;
            memberExpressionAst.Expression.Visit(this.Decorator);
            memberExpressionAst.Member.Visit(this.Decorator);
            return null;
        }

        /// <summary>
        /// Visit InvokeMemberExpression
        /// </summary>
        /// <param name="invokeMemberExpressionAst"></param>
        /// <returns></returns>
        public object VisitInvokeMemberExpression(InvokeMemberExpressionAst invokeMemberExpressionAst)
        {
            if (invokeMemberExpressionAst == null) return null;
            invokeMemberExpressionAst.Expression.Visit(this.Decorator);
            invokeMemberExpressionAst.Member.Visit(this.Decorator);
            if (invokeMemberExpressionAst.Arguments != null)
            {
                foreach (var arg in invokeMemberExpressionAst.Arguments)
                {
                    arg.Visit(this.Decorator);
                }
            }
            return null;
        }

        /// <summary>
        /// Visit Array Expression
        /// </summary>
        /// <param name="arrayExpressionAst"></param>
        /// <returns></returns>
        public object VisitArrayExpression(ArrayExpressionAst arrayExpressionAst)
        {
            if (arrayExpressionAst == null) return null;
            arrayExpressionAst.SubExpression.Visit(this.Decorator);
            return null;
        }

        /// <summary>
        /// Visit array literal
        /// </summary>
        /// <param name="arrayLiteralAst"></param>
        /// <returns></returns>
        public object VisitArrayLiteral(ArrayLiteralAst arrayLiteralAst)
        {
            if (arrayLiteralAst == null) return null;

            foreach (var element in arrayLiteralAst.Elements)
            {
                element.Visit(this.Decorator);
            }
            return null;
        }

        /// <summary>
        /// Visit Hash table
        /// </summary>
        /// <param name="hashtableAst"></param>
        /// <returns></returns>
        public object VisitHashtable(HashtableAst hashtableAst)
        {
            if (hashtableAst == null) return null;

            foreach (var pair in hashtableAst.KeyValuePairs)
            {
                pair.Item1.Visit(this.Decorator);
                pair.Item2.Visit(this.Decorator);
            }
            return null;
        }

        /// <summary>
        /// Visit ScriptBlockExpression
        /// </summary>
        /// <param name="scriptBlockExpressionAst"></param>
        /// <returns></returns>
        public object VisitScriptBlockExpression(ScriptBlockExpressionAst scriptBlockExpressionAst)
        {
            // Don't recurse into the script block, it's variables are distinct from the script block
            // we're currently analyzing.
            if (scriptBlockExpressionAst != null)
            {
                scriptBlockExpressionAst.ScriptBlock.Visit(this.Decorator);
            }
            return null;
        }

        /// <summary>
        /// Visit Paren Expression
        /// </summary>
        /// <param name="parenExpressionAst"></param>
        /// <returns></returns>
        public object VisitParenExpression(ParenExpressionAst parenExpressionAst)
        {
            if (parenExpressionAst == null) return null;

            parenExpressionAst.Pipeline.Visit(this.Decorator);
            return null;
        }

        /// <summary>
        /// Visit Expandable String Expression
        /// </summary>
        /// <param name="expandableStringExpressionAst"></param>
        /// <returns></returns>
        public object VisitExpandableStringExpression(ExpandableStringExpressionAst expandableStringExpressionAst)
        {
            if (expandableStringExpressionAst == null) return null;

            foreach (var expr in expandableStringExpressionAst.NestedExpressions)
            {
                expr.Visit(this.Decorator);
            }
            return null;
        }

        /// <summary>
        /// Visit Index Expression
        /// </summary>
        /// <param name="indexExpressionAst"></param>
        /// <returns></returns>
        public object VisitIndexExpression(IndexExpressionAst indexExpressionAst)
        {
            if (indexExpressionAst == null) return null;

            indexExpressionAst.Target.Visit(this.Decorator);
            indexExpressionAst.Index.Visit(this.Decorator);
            return null;
        }

        /// <summary>
        /// Visit Attributed Expression
        /// </summary>
        /// <param name="attributedExpressionAst"></param>
        /// <returns></returns>
        public object VisitAttributedExpression(AttributedExpressionAst attributedExpressionAst)
        {
            if (attributedExpressionAst == null) return null;

            attributedExpressionAst.Child.Visit(this.Decorator);
            return null;
        }

        /// <summary>
        /// Visit Block Statement
        /// </summary>
        /// <param name="blockStatementAst"></param>
        /// <returns></returns>
        public object VisitBlockStatement(BlockStatementAst blockStatementAst)
        {
            if (blockStatementAst == null) return null;

            blockStatementAst.Body.Visit(this.Decorator);
            return null;
        }
    }

    /// <summary>
    /// Analysis base class
    /// </summary>
    public abstract class AnalysisBase : IFlowGraph
    {
        private IFlowGraph _decorated;

        /// <summary>
        /// Create an analysis base from IFlowgraph decorated.
        /// </summary>
        /// <param name="decorated"></param>
        public AnalysisBase(IFlowGraph decorated)
        {
            if (decorated == null)
                throw new ArgumentNullException("decorated");
            this._decorated = decorated;
            this._decorated.Decorator = this;
        }

        /// <summary/>
        public virtual void Start()
        {
            _decorated.Start();
        }

        /// <summary/>
        public virtual void Stop()
        {
            _decorated.Stop();
        }

        /// <summary/>
        public virtual Block Current
        {
            get
            {
                return _decorated.Current;
            }
            set
            {
                _decorated.Current = value;
            }
        }

        /// <summary/>
        public virtual Block Entry
        {
            get
            {
                return _decorated.Entry;
            }
            set
            {
                _decorated.Entry = value;
            }
        }

        /// <summary/>
        public virtual Block Exit
        {
            get
            {
                return _decorated.Exit;
            }
            set
            {
                _decorated.Exit = value;
            }
        }

        /// <summary/>
        public virtual void Init()
        {
            _decorated.Init();
        }

        /// <summary/>
        public IFlowGraph Decorator
        {
            set
            {
                _decorated.Decorator = value;
            }
            get
            {
                return _decorated.Decorator;
            }
        }

        /// <summary/>
        public virtual object VisitErrorStatement(ErrorStatementAst errorStatementAst)
        {
            return _decorated.VisitErrorStatement(errorStatementAst);
        }

        /// <summary/>
        public virtual object VisitErrorExpression(ErrorExpressionAst errorExpressionAst)
        {
            return _decorated.VisitErrorExpression(errorExpressionAst);
        }

        /// <summary/>
        public virtual object VisitScriptBlock(ScriptBlockAst scriptBlockAst)
        {
            return _decorated.VisitScriptBlock(scriptBlockAst);
        }

        /// <summary/>
        public virtual object VisitParamBlock(ParamBlockAst paramBlockAst)
        {
            return _decorated.VisitParamBlock(paramBlockAst);
        }

        /// <summary/>
        public virtual object VisitNamedBlock(NamedBlockAst namedBlockAst)
        {
            return _decorated.VisitNamedBlock(namedBlockAst);
        }

        /// <summary/>
        public virtual object VisitTypeConstraint(TypeConstraintAst typeConstraintAst)
        {
            return _decorated.VisitTypeConstraint(typeConstraintAst);
        }

        /// <summary/>
        public virtual object VisitAttribute(AttributeAst attributeAst)
        {
            return _decorated.VisitAttribute(attributeAst);
        }

        /// <summary/>
        public virtual object VisitNamedAttributeArgument(NamedAttributeArgumentAst namedAttributeArgumentAst)
        {
            return _decorated.VisitNamedAttributeArgument(namedAttributeArgumentAst);
        }

        /// <summary/>
        public virtual object VisitParameter(ParameterAst parameterAst)
        {
            return _decorated.VisitParameter(parameterAst);
        }

        /// <summary/>
        public virtual object VisitFunctionDefinition(FunctionDefinitionAst functionDefinitionAst)
        {
            return _decorated.VisitFunctionDefinition(functionDefinitionAst);
        }

        /// <summary/>
        public virtual object VisitStatementBlock(StatementBlockAst statementBlockAst)
        {
            return _decorated.VisitStatementBlock(statementBlockAst);
        }

        /// <summary/>
        public virtual object VisitIfStatement(IfStatementAst ifStmtAst)
        {
            return _decorated.VisitIfStatement(ifStmtAst);
        }

        /// <summary/>
        public virtual object VisitTrap(TrapStatementAst trapStatementAst)
        {
            return _decorated.VisitTrap(trapStatementAst);
        }

        /// <summary/>
        public virtual object VisitSwitchStatement(SwitchStatementAst switchStatementAst)
        {
            return _decorated.VisitSwitchStatement(switchStatementAst);
        }

        /// <summary/>
        public virtual object VisitDataStatement(DataStatementAst dataStatementAst)
        {
            return _decorated.VisitDataStatement(dataStatementAst);
        }

        /// <summary/>
        public virtual object VisitForEachStatement(ForEachStatementAst forEachStatementAst)
        {
            return _decorated.VisitForEachStatement(forEachStatementAst);
        }

        /// <summary/>
        public virtual object VisitDoWhileStatement(DoWhileStatementAst doWhileStatementAst)
        {
            return _decorated.VisitDoWhileStatement(doWhileStatementAst);
        }

        /// <summary/>
        public virtual object VisitDoUntilStatement(DoUntilStatementAst doUntilStatementAst)
        {
            return _decorated.VisitDoUntilStatement(doUntilStatementAst);
        }

        /// <summary/>
        public virtual object VisitForStatement(ForStatementAst forStatementAst)
        {
            return _decorated.VisitForStatement(forStatementAst);
        }

        /// <summary/>
        public virtual object VisitWhileStatement(WhileStatementAst whileStatementAst)
        {
            return _decorated.VisitWhileStatement(whileStatementAst);
        }

        /// <summary/>
        public virtual object VisitCatchClause(CatchClauseAst catchClauseAst)
        {
            return _decorated.VisitCatchClause(catchClauseAst);
        }

        /// <summary/>
        public virtual object VisitTryStatement(TryStatementAst tryStatementAst)
        {
            return _decorated.VisitTryStatement(tryStatementAst);
        }

        /// <summary/>
        public virtual object VisitBreakStatement(BreakStatementAst breakStatementAst)
        {
            return _decorated.VisitBreakStatement(breakStatementAst);
        }

        /// <summary/>
        public virtual object VisitContinueStatement(ContinueStatementAst continueStatementAst)
        {
            return _decorated.VisitContinueStatement(continueStatementAst);
        }

        /// <summary/>
        public virtual object VisitReturnStatement(ReturnStatementAst returnStatementAst)
        {
            return _decorated.VisitReturnStatement(returnStatementAst);
        }

        /// <summary/>
        public virtual object VisitExitStatement(ExitStatementAst exitStatementAst)
        {
            return _decorated.VisitExitStatement(exitStatementAst);
        }

        /// <summary/>
        public virtual object VisitThrowStatement(ThrowStatementAst throwStatementAst)
        {
            return _decorated.VisitThrowStatement(throwStatementAst);
        }

        /// <summary/>
        public virtual object VisitAssignmentStatement(AssignmentStatementAst assignmentStatementAst)
        {
            return _decorated.VisitAssignmentStatement(assignmentStatementAst);
        }

        /// <summary/>
        public virtual object VisitPipeline(PipelineAst pipelineAst)
        {
            return _decorated.VisitPipeline(pipelineAst);
        }

        /// <summary/>
        public virtual object VisitCommand(CommandAst commandAst)
        {
            return _decorated.VisitCommand(commandAst);
        }

        /// <summary/>
        public virtual object VisitCommandExpression(CommandExpressionAst commandExpressionAst)
        {
            return _decorated.VisitCommandExpression(commandExpressionAst);
        }

        /// <summary/>
        public virtual object VisitCommandParameter(CommandParameterAst commandParameterAst)
        {
            return _decorated.VisitCommandParameter(commandParameterAst);
        }

        /// <summary/>
        public virtual object VisitFileRedirection(FileRedirectionAst fileRedirectionAst)
        {
            return _decorated.VisitFileRedirection(fileRedirectionAst);
        }

        /// <summary/>
        public virtual object VisitMergingRedirection(MergingRedirectionAst mergingRedirectionAst)
        {
            return _decorated.VisitMergingRedirection(mergingRedirectionAst);
        }

        /// <summary/>
        public virtual object VisitBinaryExpression(BinaryExpressionAst binaryExpressionAst)
        {
            return _decorated.VisitBinaryExpression(binaryExpressionAst);
        }

        /// <summary/>
        public virtual object VisitUnaryExpression(UnaryExpressionAst unaryExpressionAst)
        {
            return _decorated.VisitUnaryExpression(unaryExpressionAst);
        }

        /// <summary/>
        public virtual object VisitConvertExpression(ConvertExpressionAst convertExpressionAst)
        {
            return _decorated.VisitConvertExpression(convertExpressionAst);
        }

        /// <summary/>
        public virtual object VisitConstantExpression(ConstantExpressionAst constantExpressionAst)
        {
            return _decorated.VisitConstantExpression(constantExpressionAst);
        }

        /// <summary/>
        public virtual object VisitStringConstantExpression(StringConstantExpressionAst stringConstantExpressionAst)
        {
            return _decorated.VisitStringConstantExpression(stringConstantExpressionAst);
        }

        /// <summary/>
        public virtual object VisitSubExpression(SubExpressionAst subExpressionAst)
        {
            return _decorated.VisitSubExpression(subExpressionAst);
        }

        /// <summary/>
        public virtual object VisitUsingExpression(UsingExpressionAst usingExpressionAst)
        {
            return _decorated.VisitUsingExpression(usingExpressionAst);
        }

        /// <summary/>
        public virtual object VisitVariableExpression(VariableExpressionAst variableExpressionAst)
        {
            return _decorated.VisitVariableExpression(variableExpressionAst);
        }

        /// <summary/>
        public virtual object VisitTypeExpression(TypeExpressionAst typeExpressionAst)
        {
            return _decorated.VisitTypeExpression(typeExpressionAst);
        }

        /// <summary/>
        public virtual object VisitMemberExpression(MemberExpressionAst memberExpressionAst)
        {
            return _decorated.VisitMemberExpression(memberExpressionAst);
        }

        /// <summary/>
        public virtual object VisitInvokeMemberExpression(InvokeMemberExpressionAst invokeMemberExpressionAst)
        {
            return _decorated.VisitInvokeMemberExpression(invokeMemberExpressionAst);
        }

        /// <summary/>
        public virtual object VisitArrayExpression(ArrayExpressionAst arrayExpressionAst)
        {
            return _decorated.VisitArrayExpression(arrayExpressionAst);
        }

        /// <summary/>
        public virtual object VisitArrayLiteral(ArrayLiteralAst arrayLiteralAst)
        {
            return _decorated.VisitArrayLiteral(arrayLiteralAst);
        }

        /// <summary/>
        public virtual object VisitHashtable(HashtableAst hashtableAst)
        {
            return _decorated.VisitHashtable(hashtableAst);
        }

        /// <summary/>
        public virtual object VisitScriptBlockExpression(ScriptBlockExpressionAst scriptBlockExpressionAst)
        {
            return _decorated.VisitScriptBlockExpression(scriptBlockExpressionAst);
        }

        /// <summary/>
        public virtual object VisitParenExpression(ParenExpressionAst parenExpressionAst)
        {
            return _decorated.VisitParenExpression(parenExpressionAst);
        }

        /// <summary/>
        public virtual object VisitExpandableStringExpression(ExpandableStringExpressionAst expandableStringExpressionAst)
        {
            return _decorated.VisitExpandableStringExpression(expandableStringExpressionAst);
        }

        /// <summary/>
        public virtual object VisitIndexExpression(IndexExpressionAst indexExpressionAst)
        {
            return _decorated.VisitIndexExpression(indexExpressionAst);
        }

        /// <summary/>
        public virtual object VisitAttributedExpression(AttributedExpressionAst attributedExpressionAst)
        {
            return _decorated.VisitAttributedExpression(attributedExpressionAst);
        }

        /// <summary/>
        public virtual object VisitBlockStatement(BlockStatementAst blockStatementAst)
        {
            return _decorated.VisitBlockStatement(blockStatementAst);
        }
    }
}
