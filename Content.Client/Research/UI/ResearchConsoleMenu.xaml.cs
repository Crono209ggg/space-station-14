using System.Collections.Generic;
using Content.Shared.Research.Prototypes;
using Robust.Client.AutoGenerated;
using Robust.Client.Graphics;
using Robust.Client.UserInterface.Controls;
using Robust.Client.UserInterface.CustomControls;
using Robust.Client.UserInterface.XAML;
using Robust.Client.Utility;
using Robust.Shared.IoC;
using Robust.Shared.Localization;
using Robust.Shared.Prototypes;

namespace Content.Client.Research.UI
{
    [GenerateTypedNameReferences]
    public sealed partial class ResearchConsoleMenu : DefaultWindow
    {
        public ResearchConsoleBoundUserInterface Owner { get; }

        private readonly List<TechnologyPrototype> _unlockedTechnologyPrototypes = new();
        private readonly List<TechnologyPrototype> _unlockableTechnologyPrototypes = new();
        private readonly List<TechnologyPrototype> _futureTechnologyPrototypes = new();

        public TechnologyPrototype? TechnologySelected;

        public ResearchConsoleMenu(ResearchConsoleBoundUserInterface owner)
        {
            RobustXamlLoader.Load(this);
            IoCManager.InjectDependencies(this);

            Owner = owner;

            UnlockedTechnologies.OnItemSelected += UnlockedTechnologySelected;
            UnlockableTechnologies.OnItemSelected += UnlockableTechnologySelected;
            FutureTechnologies.OnItemSelected += FutureTechnologySelected;

            PointLabel.Text = Loc.GetString("research-console-menu-research-points-text", ("points", 0));
            PointsPerSecondLabel.Text = Loc.GetString("research-console-menu-points-per-second-text", ("pointsPerSecond", 0));
            PointLimitLabel.Text = Loc.GetString("research-console-menu-points-limit-text", ("pointsLimit", 0));

            UnlockButton.Text = Loc.GetString("research-console-menu-server-unlock-button");

            UnlockButton.OnPressed += _ =>
            {
                CleanSelectedTechnology();
            };

            Populate();
        }

        /// <summary>
        ///     Cleans the selected technology controls to blank.
        /// </summary>
        private void CleanSelectedTechnology()
        {
            UnlockButton.Disabled = true;
            TechnologyIcon.Texture = Texture.Transparent;
            TechnologyName.Text = string.Empty;
            TechnologyDescription.Text = string.Empty;
            TechnologyRequirements.Text = string.Empty;
        }

        /// <summary>
        ///     Called when an unlocked technology is selected.
        /// </summary>
        private void UnlockedTechnologySelected(ItemList.ItemListSelectedEventArgs obj)
        {
            TechnologySelected = _unlockedTechnologyPrototypes[obj.ItemIndex];

            UnlockButton.Disabled = true;

            PopulateSelectedTechnology();
        }

        /// <summary>
        ///     Called when an unlockable technology is selected.
        /// </summary>
        private void UnlockableTechnologySelected(ItemList.ItemListSelectedEventArgs obj)
        {
            TechnologySelected = _unlockableTechnologyPrototypes[obj.ItemIndex];

            UnlockButton.Disabled = Owner.Points < TechnologySelected.RequiredPoints;

            PopulateSelectedTechnology();
        }

        /// <summary>
        ///     Called when a future technology is selected
        /// </summary>
        private void FutureTechnologySelected(ItemList.ItemListSelectedEventArgs obj)
        {
            TechnologySelected = _futureTechnologyPrototypes[obj.ItemIndex];

            UnlockButton.Disabled = true;

            PopulateSelectedTechnology();
        }

        /// <summary>
        ///     Populate all technologies in the ItemLists.
        /// </summary>
        public void PopulateItemLists()
        {
            UnlockedTechnologies.Clear();
            UnlockableTechnologies.Clear();
            FutureTechnologies.Clear();

            _unlockedTechnologyPrototypes.Clear();
            _unlockableTechnologyPrototypes.Clear();
            _futureTechnologyPrototypes.Clear();

            var prototypeMan = IoCManager.Resolve<IPrototypeManager>();

            // For now, we retrieve all technologies. In the future, this should be changed.
            foreach (var tech in prototypeMan.EnumeratePrototypes<TechnologyPrototype>())
            {
                var techName = GetTechName(tech);
                if (Owner.IsTechnologyUnlocked(tech))
                {
                    UnlockedTechnologies.AddItem(techName, tech.Icon.Frame0());
                    _unlockedTechnologyPrototypes.Add(tech);
                }
                else if (Owner.CanUnlockTechnology(tech))
                {
                    UnlockableTechnologies.AddItem(techName, tech.Icon.Frame0());
                    _unlockableTechnologyPrototypes.Add(tech);
                }
                else
                {
                    FutureTechnologies.AddItem(techName, tech.Icon.Frame0());
                    _futureTechnologyPrototypes.Add(tech);
                }
            }
        }

        private string GetTechName(TechnologyPrototype prototype)
        {
            if (prototype.Name is { } name)
                return Loc.GetString(name);

            return prototype.ID;
        }

        /// <summary>
        ///     Fills the selected technology controls with details.
        /// </summary>
        public void PopulateSelectedTechnology()
        {
            if (TechnologySelected == null)
            {
                TechnologyName.Text = string.Empty;
                TechnologyDescription.Text = string.Empty;
                TechnologyRequirements.Text = string.Empty;
                return;
            }

            TechnologyIcon.Texture = TechnologySelected.Icon.Frame0();
            TechnologyName.Text = GetTechName(TechnologySelected);
            var desc = Loc.GetString(TechnologySelected.Description);
            TechnologyDescription.Text = desc + $"\n{TechnologySelected.RequiredPoints} " + Loc.GetString("research-console-menu-research-points-text" ,("points", Owner.Points)).ToLowerInvariant();
            TechnologyRequirements.Text = Loc.GetString("research-console-tech-requirements-none");

            var prototypeMan = IoCManager.Resolve<IPrototypeManager>();

            for (var i = 0; i < TechnologySelected.RequiredTechnologies.Count; i++)
            {
                var requiredId = TechnologySelected.RequiredTechnologies[i];
                if (!prototypeMan.TryIndex(requiredId, out TechnologyPrototype? prototype)) continue;
                var protoName = GetTechName(prototype);
                if (i == 0)
                    TechnologyRequirements.Text = Loc.GetString("research-console-tech-requirements-prototype-name", ("prototypeName", protoName));
                else
                    TechnologyRequirements.Text += $", {protoName}";
            }
        }

        /// <summary>
        ///     Updates the research point labels.
        /// </summary>
        public void PopulatePoints()
        {
            PointLabel.Text = Loc.GetString("research-console-menu-research-points-text", ("points", Owner.Points));
            PointsPerSecondLabel.Text = Loc.GetString("research-console-menu-points-per-second-text", ("pointsPerSecond", Owner.PointsPerSecond));
            PointLimitLabel.Text = Loc.GetString("research-console-menu-points-limit-text", ("pointsLimit", Owner.PointLimit));
        }

        /// <summary>
        ///     Updates the whole user interface.
        /// </summary>
        public void Populate()
        {
            PopulatePoints();
            PopulateSelectedTechnology();
            PopulateItemLists();
        }
    }
}
