using System.Collections.Generic;
using System.IO;
using JetBrains.Annotations;
using TMPro;
using UnityEngine;

[UsedImplicitly]
public class Driver : MonoBehaviour
{
    public static Driver Singleton;

    public Story Story;

    public TMP_Text Heading;
    public TMP_Text Text;
    public GameObject UndoButton;

    // Start is called before the first frame update
    [UsedImplicitly]
    private void Start()
    {
        Imaginarium.Driver.DataFiles.DataHome = Application.dataPath;
        Story = new Story(Path.Combine(Application.dataPath, "Story"));
        Singleton = this;
        ShowText(false);
        UndoButton.SetActive(false);
    }

    [UsedImplicitly]
    private void OnGUI()
    {
        if (Event.current.type == EventType.KeyDown)
            switch (Event.current.keyCode)
            {
                case KeyCode.Return:
                case KeyCode.Space:
                    ShowText();
                    break;

                case KeyCode.Backspace:
                    Back();
                    break;
            }
    }

    private void Back()
    {
        if (undoStack.Count > 0)
            Undo();
    }

    private void ShowText(bool saveUndo = true)
    {
        if (saveUndo)
            SaveStateForUndo();
        Story.EnactSceneTransition();
        Heading.text = Story.CurrentHeading;
        Text.text = Story.CurrentText;
    }

    private static readonly Dictionary<string, object> LinkTable = new Dictionary<string, object>();
    private static int linkCounter;

    public static string MakeLink(object target)
    {
        var id = (linkCounter++).ToString();
        LinkTable[id] = target;
        return $"<link={id}>";
    }

    public void OnClick(string linkId)
    {
        SaveStateForUndo();
        Debug.Log($"Click {linkId} => {LinkTable[linkId]}");
        Text.text = Story.Call("Click", LinkTable[linkId]);
    }

    private readonly struct GameState
    {
        public readonly Story.StoryState StoryState;
        public readonly string Heading;
        public readonly string Text;

        public GameState(Story.StoryState storyState, string heading, string text)
        {
            StoryState = storyState;
            Heading = heading;
            Text = text;
        }
    }

    private GameState CurrentGameState => new GameState(Story.CurrentStoryState, Heading.text, Text.text);

    private readonly Stack<GameState> undoStack = new Stack<GameState>();

    private void SaveStateForUndo()
    {
        UndoButton.SetActive(true);
        undoStack.Push(CurrentGameState);
    }

    public void Undo()
    {
        var gameState = undoStack.Pop();
        Heading.text = gameState.Heading;
        Text.text = gameState.Text;
        Story.SetStoryState(gameState.StoryState);
        UndoButton.SetActive(undoStack.Count > 0);
    }

}
