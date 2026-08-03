using Cysharp.Threading.Tasks;
using Sirenix.OdinInspector;
using UnityEngine;

namespace Hlight.DataPersistence.Example
{
    /// <summary>
    /// ScriptableObject that owns the game's <see cref="GameSaveBundle"/> and
    /// exposes inspector buttons for load/save during development.
    ///
    /// <para>
    /// Setup:
    /// <list type="number">
    ///   <item>Create via <b>Assets → Create → ScriptableObject</b> and select this type.</item>
    ///   <item>Assign a <see cref="FileRepository"/> or <see cref="PlayerPrefsRepository"/>
    ///         asset to the bundle's <c>Repository</c> field.</item>
    ///   <item>Expand <c>Settings → Default Value</c> and tune the starting values.</item>
    /// </list>
    /// </para>
    /// </summary>
    public class GameSaveAsset : ScriptableObject
    {
        [SerializeField] private GameSaveBundle _bundle;

        [Button("Load All"), ButtonGroup]
        private void LoadAllFromRepository() => LoadAllAsync().Forget();

        [Button("Save All"), ButtonGroup]
        private void SaveAllToRepository() => SaveAllAsync().Forget();

        private async UniTaskVoid LoadAllAsync()
        {
            await _bundle.LoadAllAsync();
            var progress = _bundle.PlayerSave.CurrentValue;
            Debug.Log($"[{nameof(GameSaveAsset)}] Loaded — Level {progress?.HighestLevelCleared}, Stars {progress?.TotalStarsEarned}");
        }

        private async UniTaskVoid SaveAllAsync()
        {
            await _bundle.SaveAllAsync();
            Debug.Log($"[{nameof(GameSaveAsset)}] Saved.");
        }
    }
}
