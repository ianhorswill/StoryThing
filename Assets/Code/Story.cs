using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Imaginarium.Generator;
using Imaginarium.Ontology;
using Step;
using Step.Interpreter;
using UnityEngine;
using static Step.Interpreter.PrimitiveTask;
using static System.IO.Directory;

public class Story
{
    public readonly string Name;
    public readonly Module Library;
    public readonly Dictionary<string, Module> Scenes = new Dictionary<string, Module>();
    public string CurrentSceneName;
    private string nextSceneName;

    public readonly struct StoryState
    {
        public readonly string SceneName;
        public readonly string NextSceneName;
        public readonly State State;

        public StoryState(string sceneName, string nextSceneName, State state)
        {
            SceneName = sceneName;
            State = state;
            NextSceneName = nextSceneName;
        }
    }

    public StoryState CurrentStoryState => new StoryState(CurrentSceneName, nextSceneName, CurrentStepState);

    public void SetStoryState(StoryState storyState)
    {
        CurrentSceneName = storyState.SceneName;
        nextSceneName = storyState.NextSceneName;
        CurrentStepState = storyState.State;
    }

    public Module CurrentScene => Scenes[CurrentSceneName];

    public string CurrentHeading => Call("Heading");

    public string CurrentText => Call("Text");

    public readonly Ontology Ontology;
    public readonly string Directory;
    
    public Story(string directory)
    {
        Directory = directory;
        Name = Path.GetFileNameWithoutExtension(directory);
        Ontology = new Ontology(Name, Path.Combine(directory, "Generator"));
        Library = MakeModule(Path.Combine(Directory, "Library"), Module.Global);
        foreach (var sceneDir in GetDirectories(Path.Combine(Directory, "Scenes")))
        {
            var sceneName = Path.GetFileNameWithoutExtension(sceneDir);
            var scene = LogWarnings(MakeModule(sceneDir, Library));
            Scenes[sceneName] = scene;
            if (scene.Defines("BeginningOfStory")  && CurrentSceneName == null)
                CurrentSceneName = sceneName;
            if (scene.Defines("DebugHere"))
            {
                CurrentSceneName = sceneName;
                scene.Call("DebugHere");
            }

        }
    }

    public void EnactSceneTransition()
    {
        if (nextSceneName == null)
            return;
        CurrentSceneName = nextSceneName;
        nextSceneName = null;
        try
        {
            Call("Start");
        } catch (UndefinedVariableException)
        { }
    }

    private Module MakeModule(string path, Module parent)
    {
        var m = new Module(Path.GetFileNameWithoutExtension(path),parent);
        m.AddBindHook(BindImaginarium);
        LoadSubtree(m, path);
        return m;
    }

    private static Module LogWarnings(Module m)
    {
        foreach (var w in m.Warnings())
            Debug.LogWarning(w);
        return m;
    }

    private void LoadSubtree(Module m, string dir)
    {
        foreach (var f in GetFiles(dir))
            if (Path.GetExtension(f) == ".step")
                m.LoadDefinitions(f);
        foreach (var sub in GetDirectories(dir))
            LoadSubtree(m, sub);
    }

    public PossibleIndividual CurrentFocus;
    public State CurrentStepState = State.Empty;

    bool BindImaginarium(StateVariableName v, out object value)
    {
        var name = v.Name;

        if ((value = BuiltinBinding(name)) != null)
            return true;

        if ((value = ConceptBinding(name)) != null)
            return true;

        return false;
    }

    private object BuiltinBinding(string name)
    {
        switch (name)
        {
            case "Generate":
                return GeneralRelation<CommonNoun, PossibleIndividual>("Generate", null, Generate, null, null);

            case "Description":
                return DeterministicText<PossibleIndividual>("Description", i => new [] { i.Description().Trim() });
            
            case "Kind":
                return DeterministicText<PossibleIndividual>("Kind",
                    i => i.MostSpecificNouns().First().SingularForm);

            case "FullName":
                return DeterministicText<PossibleIndividual>("FullName", i => new [] { i.NameString() });

            case "Nouns":
                return DeterministicText<PossibleIndividual>("Nouns", i => new [] { i.NounsString() });

            case "Adjectives":
                return DeterministicText<PossibleIndividual>("Adjectives", i => new [] { i.AdjectivesString() });

            case "IsA":
                return GeneralRelation<PossibleIndividual, MonadicConcept>("IsA",
                    (i, c) => i.IsA(c),
                    i => i.Ontology.AllCommonNouns.Where(i.IsA),
                    null,
                    null);

            case "PartOf":
                return (MetaTask)PartOfImplementation;

            case "Log":
                string Stringify(object o)
                {
                    var s = (o == null) ? "null" : o.ToString();
                    if (s != "")
                        return s;
                    if (o is PossibleIndividual i)
                        return $"a {i.MostSpecificNouns().First()}";
                    // ReSharper disable once PossibleNullReferenceException
                    return o.GetType().Name;
                }

                return (PredicateN) ((args, e) =>
                {
                    Debug.Log(e.ResolveList(args).Select(Stringify).Untokenize());
                    return true;
                });

            case "StartBold":
                return FixedWordGenerator("<b>");

            case "EndBold":
                return FixedWordGenerator("</b>");

            case "StartLink":
                return (DeterministicTextGenerator1) (o => new [] { Driver.MakeLink(o) });
            case "EndLink":
                return FixedWordGenerator("</link>");

            case "NextScene":
                return DeterministicText<string[]>("NextScene", sceneNameTokens =>
                {
                    var sceneName = sceneNameTokens.Untokenize();
                    Debug.Log($"Next scene {sceneName}");
                    if (!Scenes.ContainsKey(sceneName))
                        throw new ArgumentException($"Tried to switch to non-existent scene '{sceneName}'");
                    nextSceneName = sceneName;
                    return new string[0];
                });

            default:
                return null;
        }
    }

    private IEnumerable<PossibleIndividual> Generate(CommonNoun noun)
    {
        var g = noun.MakeGenerator();
        for (var i = 0; i < 100; i++)
            yield return g.Generate()[0];
    }

    private DeterministicTextGenerator0 FixedWordGenerator(string s) => () => new[] {s};

    private bool PartOfImplementation(object[] args, PartialOutput o, BindingEnvironment e, Step.Interpreter.Step.Continuation k)
    {
        // [PartOf ?container ?partName ?partIndividual]
        ArgumentCountException.Check("PartOf", 3, args);
        CheckArgument<PossibleIndividual>("PartOf", args[0], e, args, out var containerValue, out var containerVariable);
        var partArg = args[1];
        // ReSharper disable UnusedVariable
        CheckArgument<Part>("PartOf", partArg, e, args, out var partTypeValue, out var partTypeVariable);
        // ReSharper restore UnusedVariable
        CheckArgument<PossibleIndividual>("PartOf", args[2], e, args, out var partValue, out var partVariable);
        if (containerVariable == null)
        {
            // container in
            if (partVariable == null)
            {
                // part in
                return partValue.Individual.Container == containerValue.Individual
                    && e.Unify(partArg, partValue.Individual.ContainerPart, e.Unifications, out var bindings)
                    && k(o, bindings, e.State);
            }
            else
            {
                var invention = containerValue.Invention;
                // Container in, part out
                foreach (var pair in containerValue.Individual.Parts)
                {
                    foreach (var partIndividual in pair.Value)
                    {
                        var possibleIndividual = invention.PossibleIndividual(partIndividual);
                        var part = partIndividual.ContainerPart;
                        if (e.Unify(part, partArg, e.Unifications, out var bindings)
                            && k(o, bindings.Bind(partVariable, possibleIndividual), e.State))
                            return true;
                    }
                }

                return false;
            }
        }

        // container out
        if (partVariable == null)
        {
            var invention = partValue.Invention;
            // part in
            return e.Unify(partArg, partValue.Individual.ContainerPart, e.Unifications, out var bindingList)
                   && k(o, bindingList.Bind(containerVariable, invention.PossibleIndividual(partValue.Individual.Container)), 
                        e.State);
        }

        // container out, part out
        throw new ArgumentInstantiationException("PartOf", e, args);
    }

    private object ConceptBinding(string name)
    {
        var camelCase = ConvertCamelCase(name);
        var concept = Ontology.Part(camelCase) ??Ontology[camelCase];

        if (concept == null)
            return null;

        switch (concept)
        {
            case CommonNoun n:
                DefineSurrogate(concept,
                    Predicate<PossibleIndividual>(name, i => i.IsA(n)));
                break;

            case Adjective a:
                DefineSurrogate(concept,
                    Predicate<PossibleIndividual>(name, i => i.IsA(a)));
                break;

            case Verb verb:
                DefineSurrogate(concept,
                    Predicate<PossibleIndividual, PossibleIndividual>(name, (i1, i2) => i1.RelatesTo(i2, verb)));
                break;

            case Property p:
                if (p.Type is CatSAT.NonBoolean.SMT.Float.FloatDomain)
                    DefineSurrogate(concept,
                        UnaryFunction<PossibleIndividual, float>(name, i => (float) i.PropertyValue(p)));
                else
                    DefineSurrogate(concept,
                        DeterministicText<PossibleIndividual>(name, i => new[] {i.PropertyValue(p).ToString()}));
                break;

            case Part p:
                DefineSurrogate(concept,
                    GeneralRelation<PossibleIndividual, PossibleIndividual>(name,
                        (c, part) => part.Individual.ContainerPart == p && part.Individual.Container == c.Individual,
                        c =>
                        {
                            if (c.Individual.Parts.TryGetValue(p, out var partFillers))
                                return partFillers.Select(f => c.Invention.PossibleIndividual(f));
                            throw new ArgumentException($"{c.NameString()} does not contain the part {p}");
                        },
                        part => (part.Individual.ContainerPart == p) ? new[] {part.Invention.PossibleIndividual(part.Individual.Container)} : new PossibleIndividual[0],
                        null));
                break;
        }

        return concept;
    }

    private readonly StringBuilder camelCaseTokenBuffer = new StringBuilder();
    private readonly List<string> camelCaseTokens = new List<string>();
    private string[] ConvertCamelCase(string name)
    {
        camelCaseTokens.Clear();
        camelCaseTokenBuffer.Clear();
        foreach (var c in name)
        {
            if (char.IsUpper(c))
            {
                // Start new token
                if (camelCaseTokenBuffer.Length > 0)
                    camelCaseTokens.Add(camelCaseTokenBuffer.ToString());
                camelCaseTokenBuffer.Length = 0;
            }
            camelCaseTokenBuffer.Append(char.ToLower(c));
        }
        if (camelCaseTokenBuffer.Length > 0)
            camelCaseTokens.Add(camelCaseTokenBuffer.ToString());
        return camelCaseTokens.ToArray();
    }

    public string Call(string taskName, params object[] args)
    {
        try
        {
            var (output, state) = CurrentScene.Call(CurrentStepState, taskName, args);
            CurrentStepState = state;
            return output;
        }
        catch (Exception e)
        {
            Debug.Log(Module.StackTrace);
            if (e is CallException ce)
            {
                foreach (var a in ce.Arguments)
                    if (a is PossibleIndividual i)
                        Debug.Log(i.Description("<b>", "</b>"));
            }
            throw;
        }
    }
}
