// Given a Parser Model, the Emitter generates CSharp code
// author: Christophe VG <contact@christophe.vg>

using System;
using System.IO;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Linq;

using HumanParserGenerator.Generator;

namespace HumanParserGenerator.Emitter {
  
  public class CSharp {
    
    public bool         EmitInfo  { get; set; }
    public bool         EmitRule  { get; set; }
    public List<string> Sources   { get; set; }
    public string       Namespace { get; set; }

    private Model Model;

    public CSharp Generate(Model model) {
      this.Model = model;
      return this;
    }

    public override string ToString() {
      if( this.Model == null )            { return "// no model generated";    }
      if( this.Model.Entities.Count == 0) { return "// no entities generated"; }
      return string.Join("\n\n", 
        new List<string>() { 
          this.GenerateHeader(),
          this.GenerateReferences(),
          this.GenerateNamespace(),
          this.GenerateEntities(),
          this.GenerateParsers(),
          this.GenerateExtracting(),
          this.GenerateFooter()
        }.Where(x => x != null)
      );
    }

    private string GenerateHeader() {
      string header = null;
      if( this.EmitInfo ) {
        header += @"// DO NOT EDIT THIS FILE
// This file was generated using the Human Parser Generator
// (https://github.com/christophevg/human-parser-generator)
// on " + DateTime.Now.ToLongDateString() +
        " at " + DateTime.Now.ToLongTimeString();
        if( this.Sources != null && this.Sources.Count > 0 ) {
          header += "\n// Source" + (this.Sources.Count > 1 ? "s" : "") +
            " : " + string.Join(", ", this.Sources);
        }
      }
      return header;
    }

    private string GenerateReferences() {
      return @"using System;
using System.IO;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Linq;";
    }

    private string GenerateNamespace() {
      if(this.Namespace == null) { return null; }
      return "namespace " + this.Namespace + " {";
    }

    private string GenerateEntities() {
      return string.Join( "\n\n",
        this.Model.Entities.Select(x => this.GenerateEntity(x))
      );
    }

    private string GenerateEntity(Entity entity) {
      return string.Join( "",
        new List<string>() {
          this.GenerateSignature(entity),
          this.GenerateProperties(entity),
          this.GenerateConstructor(entity),
          this.GenerateToString(entity),
          this.GenerateEntityFooter(entity)
        }.Where(x => x != null)
      );
    }

    private string GenerateSignature(Entity entity) {
      return this.GenerateRule(entity.Rule) +
        "public " + ( entity.IsVirtual ? "interface" : "class" ) + " " +
        Format.CSharp.Class(entity) + 
        ( entity.Supers.Where(s=>s.IsVirtual).Count() > 0 ?
          " : " + string.Join( ", ",
            entity.Supers.Where(s=> s.IsVirtual)
              .Select(x => Format.CSharp.Class(x))
          )
          : ""
        ) + " {";
    }

    private string GenerateProperties(Entity entity) {
      if(entity.IsVirtual) { return null; }
      return "\n" + string.Join("\n",
        entity.Properties.Select(x => this.GenerateProperty(x))
      );
    }

    private string GenerateProperty(Property property) {
      return "public " + Format.CSharp.Type(property) + " " + 
        Format.CSharp.Property(property) + " { get; set; }";
    }

    private string GenerateConstructor(Entity entity) {
      if(entity.IsVirtual) { return null; }
      if( ! entity.HasPluralProperty() ) { return null; }
      return "\n  public " + Format.CSharp.Class(entity) + "() {\n" +
        string.Join("\n",
          entity.Properties.Where(x => x.IsPlural || x.Source.HasPluralParent).Select(x => 
            "this." + Format.CSharp.Property(x) + " = new " + 
              Format.CSharp.Type(x) + "();\n"
          )
        ) +
        "}";
    }

    private string GenerateToString(Entity entity) {
      if(entity.IsVirtual) { return null; }
      return "\n  public override string ToString() {\n" +
        "return\n" +
        "\"new " + Format.CSharp.Class(entity) +
        "() { " + (entity.Properties.Count > 1 ? "\\n" : "") + "\" +\n" + 
        string.Join(" + \",\\n\" +\n",
          entity.Properties.Select(x => this.GenerateToString(x))
        ) + ( entity.Properties.Count > 0 ? " + \n" : "" ) +
        "\"}\";\n" +
        "}\n";
    }

    private string GenerateToString(Property property) {
      if(property.IsPlural || property.Source.HasPluralParent) {
        return string.Format(
          "\"{0} = new " + Format.CSharp.Type(property) + "() {{\" + \nstring.Join(\",\", " +
          "this.{0}.Select(x => x.ToString())) +\n" +
          "\"}}\"",
          Format.CSharp.Property(property)
        );
      } else {
        if(property.Type.Equals("<string>")) {
          // "Property = \"" + this.Property + "\""
          return string.Format(
            @"""{0} = "" + Format.Literal(this.{0})",
            Format.CSharp.Property(property)
          );
        } else if(property.Type.Equals("<bool>")){
          // "Property = this.Property"
          return string.Format(
            @"""{0} = this.{0}""",
            Format.CSharp.Property(property)
          );   
        } else {
          // "Property =
          // ( this.Property" == null ? \"null\" : this.Property.ToString() )
          return string.Format(
            @"""{0} = "" + ( this.{0} == null ? ""null"" : this.{0}.ToString() )",
            Format.CSharp.Property(property)
          );
        }
      }
    }

    private string GenerateEntityFooter(Entity entity) {
      return "}";
    }

    // PARSER METHODS

    private string GenerateParsers() {
      return string.Join( "\n\n",
        this.GenerateParserHeader(),
        this.GenerateEntityParsers(),
        this.GenerateParserFooter()
      );
    }

    private string GenerateParserHeader() {
      return "public class Parser : ParserBase<" +
        Format.CSharp.Class(this.Model.Root) + "> {\n" +
        ( this.Model.Contains("_") && this.Model["_"].ParseAction is ConsumePattern ?
          "\npublic Parser() : base(" +
            Format.CSharp.VerbatimStringLiteral(
              "^" + ((ConsumePattern)this.Model["_"].ParseAction).Pattern
            ) +
          "){}\n"
          : ""
        );
    }
  
    private string GenerateEntityParsers() {
      return string.Join("\n\n",
        this.Model.Entities
          .Where(entity =>
            ! (entity.IsVirtual && entity.ParseAction is ConsumePattern)
          )
          .Select(x => this.GenerateEntityParser(x))
      );
    }

    private string GenerateEntityParser(Entity entity) {
      return string.Join("\n",
        new List<string>() {
          this.GenerateEntityParserHeader(entity),
          this.GenerateParseAction(entity.ParseAction),
          this.GenerateEntityParserFooter(entity)
        }
      );
    }

    private string GenerateEntityParserHeader(Entity entity) {
      return this.GenerateRule(entity.Rule) +
        "public " + (this.Model.Root == entity ? " override " : "" ) +
        Format.CSharp.Type(entity) +
        " Parse" + (this.Model.Root == entity ? "" : Format.CSharp.Class(entity) ) +
        "() {\n" +
        Format.CSharp.Type(entity) + " " + Format.CSharp.Variable(entity) +
        " = " +
        (entity.IsVirtual ? "null" : "new " + Format.CSharp.Class(entity) + "()") + ";\n" +
        "this.Log( \"Parse" + Format.CSharp.Class(entity) + "\" );\n" +
        "Parse( () => {";
    }

    private string GenerateParseAction(ParseAction action) {
      try {
        string code = new Dictionary<string, Func<ParseAction,string>>() {
          { "ConsumeString",  this.GenerateConsumeString  },
          { "ConsumePattern", this.GenerateConsumePattern },
          { "ConsumeEntity",  this.GenerateConsumeEntity  },
          { "ConsumeAll",     this.GenerateConsumeAll     },
          { "ConsumeAny",     this.GenerateConsumeAny     },
        }[action.GetType().ToString().Split('.').Last()](action);
        return this.WrapOptional(action, code);
      } catch(KeyNotFoundException e) {
        throw new NotImplementedException(
          "extracting not implemented for " + action.GetType().ToString(), e
        );
      }
    }

    private string GenerateConsumeString(ParseAction action) {
      ConsumeString consume = (ConsumeString)action;
      return this.WrapAssignment(action, 
        ( consume.IsOptional ? "Maybe" : "" ) + "Consume(\"" + consume.String + "\")"
      ) + ";";
    }

    private string GenerateConsumePattern(ParseAction action) {
      ConsumePattern consume = (ConsumePattern)action;
      return this.WrapAssignment(action,
        "Consume(Extracting." +
        Format.CSharp.Class(consume.Property.Entity) + ");"
      );
    }

    private string GenerateConsumeEntity(ParseAction action) {
      ConsumeEntity consume = (ConsumeEntity)action;
      if(consume.Property.IsPlural) {
        return Format.CSharp.EntityProperty(consume.Property) + 
          " = Many<" + Format.CSharp.Type(consume.Entity) + ">(" + 
          this.GenerateConsumeSingleEntity(consume, true, true) +
          ");";
      }
      return this.GenerateConsumeSingleEntity(consume) + ";";
    }

    private string GenerateConsumeSingleEntity(ConsumeEntity consume,
                                               bool withoutAssignment = false,
                                               bool withoutExecution  = false)
    {
      // if the referenced Entity is Virtual and is an Extractor, consume it 
      // directly
      if( consume.Entity.IsVirtual &&
          consume.Entity.ParseAction is ConsumePattern)
      {
        string code = "Consume(Extracting." + Format.CSharp.Class(consume.Entity) + ")";
        return withoutAssignment ? code : this.WrapAssignment(consume, code);
      }

      // simple case, dispatch to Parse<Entity>
      string code2 = "Parse" + Format.CSharp.Class(consume.Entity) + 
        (withoutExecution ? "" : "()");

      return withoutAssignment ? code2 : this.WrapAssignment(consume, code2);
    }

    private string GenerateConsumeAll(ParseAction action) {
      ConsumeAll consume = (ConsumeAll)action;
      return 
        ( consume.IsPlural ? "Repeat( () => {\n" : "" ) +
        string.Join("\n",
          consume.Actions.Select(next => this.GenerateParseAction(next))
        ) +
        ( consume.IsPlural ? "\n});" : "" );
    }

    private string GenerateConsumeAny(ParseAction action) {
      ConsumeAny consume = (ConsumeAny)action;
      string code = "";
      bool first = true;
      foreach(var option in consume.Actions) {
        code +=
          (first ? "Parse" : ".Or") + "( () => { \n" +
            this.GenerateParseAction(option) + "\n" +
          "})\n";
        first = false;
      }
      code += ".OrThrow(\"Expected: " + consume.Label + "\"); ";

      return 
        ( consume.IsPlural ? "Repeat( () => {\n" : "" ) +
        code +
        ( consume.IsPlural ? "\n});" : "" );
    }

    private string WrapOptional(ParseAction action, string code) {
      if( ! action.IsOptional )             { return code; }
      if( this.isTryConsumeString(action) ) { return code; }
      return "Maybe( () => {\n" + code + "\n});";
    }

    private string WrapAssignment(ParseAction action, string code) {
      if(action.Type == null)     { return code; }
      if(action.Property == null) { return code; }
      return (action.Property.Entity.IsVirtual ?
        Format.CSharp.Variable(action.Property.Entity) :
        Format.CSharp.EntityProperty(action.Property)) +
        ( action.HasPluralParent ?
          ".Add" + (action.Property.IsPlural ? "Range" : "") + "("
          : " = "
        ) + code + (action.HasPluralParent ? ")" : "");
    }

    private bool isTryConsumeString(ParseAction action) {
      return action.IsOptional && action is ConsumeString;
    }

    private string GenerateEntityParserFooter(Entity entity) {
      return "})." +
        "OrThrow(\"Failed to parse " + Format.CSharp.Class(entity) + "\");\n" +
         this.GenerateEntityParserReturn(entity);
    }

    private string GenerateEntityParserReturn(Entity entity) {
      return "return " + Format.CSharp.Variable(entity) + ";\n}";
    }

    // Extracting functionality is generated for all Entities that are "just"
    // consuming a pattern.
    private string GenerateExtracting() {
      return
        "public class Extracting {\n" +
        string.Join("\n",
          this.Model.Entities
              .Where(entity => entity.ParseAction is ConsumePattern)
              .Select(entity => 
                "public static Regex " + Format.CSharp.Class(entity) +
                 " = new Regex(" + Format.CSharp.VerbatimStringLiteral(
                   "^" + ((ConsumePattern)entity.ParseAction).Pattern
                 ) + ");"
              )
        ) + "\n" +
        "}";
    }

    private string GenerateParserFooter() {
      return "}";
    }

    private string GenerateFooter() {
      string footer = null;
      if( this.Namespace != null ) { footer += "}"; }
      return footer;
    }

    private Emitter.BNF bnf = new Emitter.BNF();

    private string GenerateRule(Rule rule) {
      if( ! this.EmitRule ) { return ""; }
      return "// " + this.bnf.GenerateRule(rule) + "\n";
    }

    // logging functionality

    private void Warn(string msg) {
      this.Log("warning: " + msg);
    }

    private void Log(string msg) {
      Console.Error.WriteLine("hpg-emitter: " + msg);
    }
  }
}
