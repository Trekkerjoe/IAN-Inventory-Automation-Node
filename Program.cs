using Sandbox.Game.EntityComponents;
using Sandbox.ModAPI.Ingame;
using Sandbox.ModAPI.Interfaces;
using SpaceEngineers.Game.ModAPI.Ingame;
using System.Collections.Generic;
using System.Collections;
using System.Linq;
using System.Text;
using System;
using VRage.Collections;
using VRage.Game.Components;
using VRage.Game.ModAPI.Ingame;
using VRage.Game.ModAPI.Ingame.Utilities;
using VRage.Game.ObjectBuilders.Definitions;
using VRage.Game;
using VRageMath;

namespace IngameScript
{
    partial class Program : MyGridProgram
    {
        //
        //INVENTORY AUTOMATION NODE
        //     BY TREKKERJOE
        //

        const string VERSION = "v0.0.1";

        const string TAG = "[IAN]";
        const string CONNECTED_PB_TAG = "[PB]";

        double maxThreshholdMs = 0.02;// The amount of Ms the code will attempt to avoid going over. These will be overwritten by the Ini in the CustomData.
        double minThreshholdMs = 0.01;// The amount of Ms the code will allow itself to run above.
        double runtimeMs;// The initial value for runtimeMs. Set as the average of maxThreshholdMS and minThreshholdMS to ensure it doesn't initially interfere with the load balancing.

        void SetInstructionLimiters()// Calculates the initial values for the instruction limiters. (The number of iterations before the program has to yield to maintain performance.)
        {
            sortInstructionCount = (int)Math.Round(100 - (Runtime.LastRunTimeMs * 100));// TODO: Adjust the algorithm to reflect a more accurate ratio of instruction cost.
            moveInstructionCount = (int)(sortInstructionCount * 0.1);
            if (sortInstructionCount < 1)
                sortInstructionCount = 1;
            scanInstructionCount = (int)(sortInstructionCount * 1.2);
            if (scanInstructionCount < 1)
                scanInstructionCount = 1;
            renderInstructionCount = scanInstructionCount;
            if (moveInstructionCount < 1)
                moveInstructionCount = 1;
        }//

        List<IMyProgrammableBlock> connectedPBs = new List<IMyProgrammableBlock>();// These will recieve arguments when particular ores or ingots drop below a quota.

        List<IMyTerminalBlock> inventories = new List<IMyTerminalBlock>();// Lists holding the various inventories.
        List<IMyRefinery> refineries = new List<IMyRefinery>();
        List<IMyRefinery> furnaces = new List<IMyRefinery>();
        const string FURNACE_TYPE_ID = "Blast Furnace";// The diferentiating subtype between arc furnaces and refineries.
        List<IMyAssembler> assemblers = new List<IMyAssembler>();
        List<IMyReactor> reactors = new List<IMyReactor>();
        List<IMyGasGenerator> gasGenerators = new List<IMyGasGenerator>();
        List<IMyTerminalBlock> gatlings = new List<IMyTerminalBlock>();
        List<IMyTerminalBlock> missileLaunchers = new List<IMyTerminalBlock>();
        List<IMyTextPanel> lcds = new List<IMyTextPanel>();


        double CalculateQuota(double totalResources, double baseQuota, double demand, float multiplier, bool disableMultiplier)// Calculates the quota of an item using it's multiplier.
        {// The algorithm that calculates quotas.
            if (disableMultiplier)
            {
                if (baseQuota >= demand)// Ensure that we reach our demand at minimum.
                    return baseQuota;
                else
                    return demand;
            }//
            double newQuota = (long)((totalResources * multiplier)+0.5f);// Where totalResources is equal to the total resources in that particular catagory.
            if (newQuota < baseQuota)// Ensure we use at least the base quota if the minimum is below
                newQuota = baseQuota;
            if (newQuota >= demand)
                return newQuota;
            else
                return demand;
        }//

        IEnumerator<bool> stateMachine;// The statemachine that will split the runtime to reduce impact.
        int sortInstructionCount;// Load balancers for code execution time. Initialized based on the initial load.
        int moveInstructionCount;
        int scanInstructionCount;
        int renderInstructionCount;

        // Structs containing a dictionary of handling information for every item in vanilla.
        // Components
        ItemInformation steelPlate = new ItemInformation() { sortOrder = 0, multiplier = 0.4f, baseQuota = 150, disableMultiplier = false, large = true },
            construction = new ItemInformation() { sortOrder = 1, multiplier = 0.2f, baseQuota = 150, disableMultiplier = false, large = false },
            interiorPlate = new ItemInformation() { sortOrder = 2, multiplier = 0.1f, baseQuota = 100, disableMultiplier = false, large = true },
            computer = new ItemInformation() { sortOrder = 3, multiplier = 0.05f, baseQuota = 30, disableMultiplier = false, large = false },
            thrust = new ItemInformation() { sortOrder = 4, multiplier = 0.05f, baseQuota = 15, disableMultiplier = false, large = false },
            motor = new ItemInformation() { sortOrder = 5, multiplier = 0.04f, baseQuota = 20, disableMultiplier = false, large = false },
            smallTube = new ItemInformation() { sortOrder = 6, multiplier = 0.03f, baseQuota = 50, disableMultiplier = false, large = true },
            bulletproofGlass = new ItemInformation() { sortOrder = 7, multiplier = 0.02f, baseQuota = 50, disableMultiplier = false, large = true },
            reactor = new ItemInformation() { sortOrder = 8, multiplier = 0.02f, baseQuota = 25, disableMultiplier = false, large = false },
            metalGrid = new ItemInformation() { sortOrder = 9, multiplier = 0.02f, baseQuota = 20, disableMultiplier = false, large = true },
            largeTube = new ItemInformation() { sortOrder = 10, multiplier = 0.02f, baseQuota = 10, disableMultiplier = false, large = true },
            powerCell = new ItemInformation() { sortOrder = 11, multiplier = 0.01f, baseQuota = 20, disableMultiplier = false, large = true },
            superconductor = new ItemInformation() { sortOrder = 12, multiplier = 0.01f, baseQuota = 10, disableMultiplier = false, large = true },
            radioCommunication = new ItemInformation() { sortOrder = 15, multiplier = 0.005f, baseQuota = 10, disableMultiplier = false, large = true },
            display = new ItemInformation() { sortOrder = 16, multiplier = 0.005f, baseQuota = 10, disableMultiplier = false, large = true },
            girder = new ItemInformation() { sortOrder = 17, multiplier = 0.005f, baseQuota = 10, disableMultiplier = false, large = true },
            solarCell = new ItemInformation() { sortOrder = 13, multiplier = 0.001f, baseQuota = 20, disableMultiplier = false, large = true },
            detector = new ItemInformation() { sortOrder = 14, multiplier = 0.001f, baseQuota = 10, disableMultiplier = false, large = true },
            medical = new ItemInformation() { sortOrder = 18, multiplier = 0.001f, baseQuota = 15, disableMultiplier = true, large = true },// Never need more than 15 medical components.
            explosives = new ItemInformation() { sortOrder = 19, multiplier = 0.001f, baseQuota = 5, disableMultiplier = false, large = false },
            gravityGenerator = new ItemInformation() { sortOrder = 20, multiplier = 0.001f, baseQuota = 1, disableMultiplier = false, large = true },
            canvas = new ItemInformation() { sortOrder = 21, multiplier = 0.0001f, baseQuota = 5, disableMultiplier = false, large = false };
        // Ingots
        ItemInformation ironIngot = new ItemInformation() { sortOrder = 0, multiplier = 0.88f, baseQuota = 200, disableMultiplier = false, large = false },
            cobaltIngot = new ItemInformation() { sortOrder = 1, multiplier = 0.035f, baseQuota = 50, disableMultiplier = false, large = false },
            stoneIngot = new ItemInformation() { sortOrder = 2, multiplier = 0.025f, baseQuota = 50, disableMultiplier = false, large = false },
            siliconIngot = new ItemInformation() { sortOrder = 3, multiplier = 0.02f, baseQuota = 50, disableMultiplier = false, large = false },
            nickelIngot = new ItemInformation() { sortOrder = 4, multiplier = 0.015f, baseQuota = 30, disableMultiplier = false, large = false },
            silverIngot = new ItemInformation() { sortOrder = 5, multiplier = 0.01f, baseQuota = 20, disableMultiplier = false, large = false },
            goldIngot = new ItemInformation() { sortOrder = 6, multiplier = 0.002f, baseQuota = 5, disableMultiplier = false, large = false },
            magnesiumIngot = new ItemInformation() { sortOrder = 7, multiplier = 0.001f, baseQuota = 5, disableMultiplier = false, large = false },
            platinumIngot = new ItemInformation() { sortOrder = 8, multiplier = 0.001f, baseQuota = 5, disableMultiplier = false, large = false },
            uraniumIngot = new ItemInformation() { sortOrder = 9, multiplier = 0.001f, baseQuota = 1, disableMultiplier = false, large = false };
        // Ores
        ItemInformation ironOre = new ItemInformation() { sortOrder = 0, multiplier = 0.0f, baseQuota = 0, disableMultiplier = true, large = false },
            cobaltOre = new ItemInformation() { sortOrder = 1, multiplier = 0.0f, baseQuota = 0, disableMultiplier = true, large = false },
            stoneOre = new ItemInformation() { sortOrder = 2, multiplier = 0.0f, baseQuota = 0, disableMultiplier = true, large = false },
            siliconOre = new ItemInformation() { sortOrder = 3, multiplier = 0.0f, baseQuota = 0, disableMultiplier = true, large = false },
            nickelOre = new ItemInformation() { sortOrder = 4, multiplier = 0.0f, baseQuota = 0, disableMultiplier = true, large = false },
            silverOre = new ItemInformation() { sortOrder = 5, multiplier = 0.0f, baseQuota = 0, disableMultiplier = true, large = false },
            goldOre = new ItemInformation() { sortOrder = 6, multiplier = 0.0f, baseQuota = 0, disableMultiplier = true, large = false },
            magnesiumOre = new ItemInformation() { sortOrder = 7, multiplier = 0.0f, baseQuota = 0, disableMultiplier = true, large = false },
            platinumOre = new ItemInformation() { sortOrder = 8, multiplier = 0.0f, baseQuota = 0, disableMultiplier = true, large = false },
            uraniumOre = new ItemInformation() { sortOrder = 9, multiplier = 0.0f, baseQuota = 0, disableMultiplier = true, large = false },
            ice = new ItemInformation() { sortOrder = 10, multiplier = 0.0f, baseQuota = 0, disableMultiplier = true, large = false },
            scrap = new ItemInformation() { sortOrder = 11, multiplier = 0.0f, baseQuota = 0, disableMultiplier = true, large = false };
        // Ammunition
        ItemInformation nato_5p56x45mm = new ItemInformation() { sortOrder = 0, multiplier = 0.0f, baseQuota = 0, disableMultiplier = true, large = false },
            nato_25x184mm = new ItemInformation() { sortOrder = 1, multiplier = 0.0f, baseQuota = 0, disableMultiplier = true, large = false },
            missile200mm = new ItemInformation() { sortOrder = 2, multiplier = 0.0f, baseQuota = 0, disableMultiplier = true, large = true };
        // Player Items //Everything below this point is not sorted, or part of any quota. 
        ItemInformation ultimateAutomaticRifleItem = new ItemInformation() { sortOrder = 0, multiplier = 0.0f, baseQuota = 0, disableMultiplier = true, large = true },
            welder4Item = new ItemInformation() { sortOrder = 1, multiplier = 0.0f, baseQuota = 0, disableMultiplier = true, large = true },
            angleGrinder4Item = new ItemInformation() { sortOrder = 2, multiplier = 0.0f, baseQuota = 0, disableMultiplier = true, large = true },
            handDrill4Item = new ItemInformation() { sortOrder = 3, multiplier = 0.0f, baseQuota = 0, disableMultiplier = true, large = true },
            rapidFireAutomaticRifleItem = new ItemInformation() { sortOrder = 4, multiplier = 0.0f, baseQuota = 0, disableMultiplier = true, large = true },
            welder3Item = new ItemInformation() { sortOrder = 5, multiplier = 0.0f, baseQuota = 0, disableMultiplier = true, large = true },
            angleGrinder3Item = new ItemInformation() { sortOrder = 6, multiplier = 0.0f, baseQuota = 0, disableMultiplier = true, large = true },
            handDrill3Item = new ItemInformation() { sortOrder = 7, multiplier = 0.0f, baseQuota = 0, disableMultiplier = true, large = true },
            preciseAutomaticRifleItem = new ItemInformation() { sortOrder = 8, multiplier = 0.0f, baseQuota = 0, disableMultiplier = true, large = true },
            welder2Item = new ItemInformation() { sortOrder = 9, multiplier = 0.0f, baseQuota = 0, disableMultiplier = true, large = true },
            angleGrinder2Item = new ItemInformation() { sortOrder = 10, multiplier = 0.0f, baseQuota = 0, disableMultiplier = true, large = true },
            handDrill2Item = new ItemInformation() { sortOrder = 11, multiplier = 0.0f, baseQuota = 0, disableMultiplier = true, large = true },
            automaticRifleItem = new ItemInformation() { sortOrder = 12, multiplier = 0.0f, baseQuota = 0, disableMultiplier = true, large = true },
            welderItem = new ItemInformation() { sortOrder = 13, multiplier = 0.0f, baseQuota = 0, disableMultiplier = true, large = true },
            angleGrinderItem = new ItemInformation() { sortOrder = 14, multiplier = 0.0f, baseQuota = 0, disableMultiplier = true, large = true },
            handDrillItem = new ItemInformation() { sortOrder = 15, multiplier = 0.0f, baseQuota = 0, disableMultiplier = true, large = true };
        // Oxygen Bottle
        ItemInformation oxygenBottle = new ItemInformation() { sortOrder = 0, multiplier = 0.0f, baseQuota = 0, disableMultiplier = true, large = true };
        // Hydrogen Bottle
        ItemInformation hydrogenBottle = new ItemInformation() { sortOrder = 0, multiplier = 0.0f, baseQuota = 0, disableMultiplier = true, large = true };

        // Dictionaries
        // Subtype reference
        Dictionary<string, ItemInformation> ores = new Dictionary<string, ItemInformation>();
        Dictionary<string, ItemInformation> ingots = new Dictionary<string, ItemInformation>();
        Dictionary<string, ItemInformation> components = new Dictionary<string, ItemInformation>();
        Dictionary<string, ItemInformation> ammoMagazines = new Dictionary<string, ItemInformation>(); 
        Dictionary<string, ItemInformation> physicalGunObjects = new Dictionary<string, ItemInformation>();
        Dictionary<string, ItemInformation> oxygenContainerObjects = new Dictionary<string, ItemInformation>();
        Dictionary<string, ItemInformation> gasContainerObjects = new Dictionary<string, ItemInformation>();// Yes, they have their own type. -_-
        // Type reference (Contains subtypes.)
        Dictionary<string, Dictionary<string, ItemInformation>> items = new Dictionary<string, Dictionary<string, ItemInformation>>();

        void BuildDictionary()// Method to build the item dictionary.
        {
            // Components
            components.Add("SteelPlate", steelPlate);
            components.Add("Construction", construction);
            components.Add("InteriorPlate", interiorPlate);
            components.Add("Computer", computer);
            components.Add("Thrus", thrust);
            components.Add("Motor", motor);
            components.Add("SmallTube", smallTube);
            components.Add("BulletproofGlass", bulletproofGlass);
            components.Add("Reactor", reactor);
            components.Add("MetalGrid", metalGrid);
            components.Add("LargeTube", largeTube);
            components.Add("PowerCell", powerCell);
            components.Add("Superconductor", superconductor);
            components.Add("RadioCommunication", radioCommunication);
            components.Add("Display", display);
            components.Add("Girder", girder);
            components.Add("SolarCell", solarCell);
            components.Add("Detector", detector);
            components.Add("Medical", medical);
            components.Add("Explosives", explosives);
            components.Add("GravityGenerator", gravityGenerator);
            components.Add("Canvas", canvas);
            // Ingots
            ingots.Add("Iron", ironIngot);
            ingots.Add("Cobalt", cobaltIngot);
            ingots.Add("Stone", stoneIngot);
            ingots.Add("Silicon", siliconIngot);
            ingots.Add("Nickel", nickelIngot);
            ingots.Add("Silver", silverIngot);
            ingots.Add("Gold", goldIngot);
            ingots.Add("Magnesium", magnesiumIngot);
            ingots.Add("Platinum", platinumIngot);
            ingots.Add("Uranium", uraniumIngot);
            // Ores
            ores.Add("Iron", ironOre);
            ores.Add("Cobalt", cobaltOre);
            ores.Add("Stone", stoneOre);
            ores.Add("Silicon", siliconOre);
            ores.Add("Nickel", nickelOre);
            ores.Add("Silver", silverOre);
            ores.Add("Gold", goldOre);
            ores.Add("Magnesium", magnesiumOre);
            ores.Add("Platinum", platinumOre);
            ores.Add("Uranium", uraniumOre);
            ores.Add("Ice", ice);
            ores.Add("Scrap", scrap);
            // Ammunition
            ammoMagazines.Add("NATO_5p56x45mm", nato_5p56x45mm);
            ammoMagazines.Add("NATO_25x184mm", nato_25x184mm);
            ammoMagazines.Add("Missile200mm", missile200mm);
            // Player Items
            physicalGunObjects.Add("AutomaticRifleItem", automaticRifleItem);
            physicalGunObjects.Add("PreciseAutomaticRifleItem", preciseAutomaticRifleItem);
            physicalGunObjects.Add("RapidFireAutomaticRifleItem", rapidFireAutomaticRifleItem);
            physicalGunObjects.Add("UltimateAutomaticRifleItem", ultimateAutomaticRifleItem);
            physicalGunObjects.Add("WelderItem", welderItem);
            physicalGunObjects.Add("Welder2Item", welder2Item);
            physicalGunObjects.Add("welder3Item", welder3Item);
            physicalGunObjects.Add("welder4Item", welder4Item);
            physicalGunObjects.Add("AngleGrinderItem", angleGrinderItem);
            physicalGunObjects.Add("AngleGrinder2Item", angleGrinder2Item);
            physicalGunObjects.Add("AngleGrinder3Item", angleGrinder3Item);
            physicalGunObjects.Add("AngleGrinder4Item", angleGrinder4Item);
            physicalGunObjects.Add("HandDrillItem", handDrillItem);
            physicalGunObjects.Add("HandDrill2Item", handDrill2Item);
            physicalGunObjects.Add("HandDrill3Item", handDrill3Item);
            physicalGunObjects.Add("HandDrill4Item", handDrill4Item);
            // Oxygen bottles
            oxygenContainerObjects.Add("OxygenBottle", oxygenBottle);
            // Hydrogen bottles
            gasContainerObjects.Add("HydrogenBottle", hydrogenBottle);
            // Type lookup
            items.Add("Ore", ores);
            items.Add("Ingot", ingots);
            items.Add("Component", components);
            items.Add("AmmoMagazine", ammoMagazines);
            items.Add("PysicalGunObject", physicalGunObjects);
            items.Add("OxygenContainerObject", oxygenContainerObjects);
            items.Add("GasContainerObject", gasContainerObjects);
        }//

        void Retrieve()// Retrieves the information of the custom data as an ini file.
        {
            /*Dictionary<string, ItemInformation> dictionary; // This is just here as a TEMPORARY example.
            ItemInformation item;
            items.TryGetValue("Ore", out dictionary);
            dictionary.TryGetValue("Iron", out item);*/
        }//

        void Store()// Stores information in the custom data as an ini file.
        {
            
        }//

        public Program()
        {
            BuildDictionary();// Initialize the dictionary for the program to use.
            Runtime.UpdateFrequency |= UpdateFrequency.Once;
            runtimeMs = ((maxThreshholdMs + minThreshholdMs) * 0.5);// RuntimeMs initializes to the average between the max in min so to not interfere with the instruction limiter on first run.
        }//

        bool init0 = true;
        bool init1 = true;
        bool active = true;
        public void Main(string argument, UpdateType updateSource)
        {
            if (init0)// Initialize the blocks and the variables.
            {
                Initialize();
                Retrieve();
                init0 = false;
                Runtime.UpdateFrequency |= UpdateFrequency.Once;
            }//
            else if (init1)// Use the LastRunTimeMs generated from the initialization run to calculate instruction limiters.
            {
                SetInstructionLimiters();
                stateMachine = IAN(updateSource);
                init1 = false;
                Runtime.UpdateFrequency |= (UpdateFrequency.Once | UpdateFrequency.Update10);
            }//
            else
            {
                if ((updateSource & (UpdateType.Once | UpdateType.Update10)) != 0)
                {
                    RunStateMachine(updateSource);
                }//
            }//
            if ((updateSource & UpdateType.Update10) != 0)
            {
                Display();
                Echo("scanCycles: " + scanInstructionCount);// TEMPORARY
                Echo("sortCycles: " + sortInstructionCount);
                Echo("moveCycles: " + moveInstructionCount);
                Echo("renderCycles: " + renderInstructionCount);
            }
            runtimeMs = Runtime.LastRunTimeMs;
        }//

        // State Machine.
        void RunStateMachine(UpdateType update)
        {
            if (stateMachine != null)
            {
                if (!stateMachine.MoveNext())
                {
                    stateMachine.Dispose();
                    stateMachine = IAN(update);
                }//
                else
                    Runtime.UpdateFrequency |= UpdateFrequency.Once;
            }//
        }//

        // Main Loop.
        IEnumerator<bool> IAN(UpdateType updateType)
        {
            if (active)// All this is supposition, and subject to change.
            {
                foreach (var scan in Scan())
                    yield return true;
                yield return true;
                CalculateQuota();
                yield return true;
                foreach (var sortRefineries in QueRefineries())
                    yield return true; 
                yield return true;
                foreach (var queAssemblers in QueAssemblers())
                    yield return true;
                QueSort();
                yield return true;
                foreach (var sort in Sort())
                    yield return true;
                yield return true;
                QueMove();
                yield return true;
                foreach (var move in Move())
                    yield return true;
                Initialize();
                yield return true;
                foreach (var render in Render())
                    yield return true;
            }//
            else
            {
                if ((updateType & UpdateType.Update100) != 0)
                    Initialize();
                if (active)
                    Runtime.UpdateFrequency |= UpdateFrequency.Once;
            }//
            yield return false;
        }//

        IEnumerable<bool> Scan()// Searches each block to determine their inventory status and que requests. Pools the inventory.
        {
            yield return true;
        }//

        void CalculateQuota()// Updates the internal Quota of the block based on the pooled inventory using ratios and demand defined by the player. Pass request arguments.
        {
            
        }//

        IEnumerable<bool> QueRefineries()// Handles the refineries to ensure we produce the highest priority ore.
        {
            int sortInstructions = 0;
            foreach (IMyRefinery refinery in refineries)
            {
                if (sortInstructions < sortInstructionCount)
                {
                    sortInstructions++;
                }//
                else
                {
                    sortInstructions = 0;
                    yield return true;
                }//
            }
        }//

        IEnumerable<bool> QueAssemblers()// Handles queing assemblers to produce missing components.
        {

            yield return true;
        }//

        void QueSort()// Que the sorting of everything else.
        {
            
        }//

        IEnumerable<bool> Sort()// Handles the sorting of everything else.
        {
            int sortInstructions = 0;
            if (sortInstructions < sortInstructionCount)
            {
                sortInstructions++;
            }//
            else
            {
                sortInstructions = 0;
                yield return true;
            }//
        }//

        void QueMove()// Handles the moving of inventory items between inventories. Stockpiles have priority.
        {

        }//

        IEnumerable<bool> Move()// Handles the moving of resources between inventories.
        {
            int moveInstructions = 0;
            if (moveInstructions < moveInstructionCount)
            {
                moveInstructions++;
            }//
            else
            {
                moveInstructions = 0;
                yield return true;
            }//
            yield return true;
        }//

        List<IMyTerminalBlock> blocks = new List<IMyTerminalBlock>();
        void Initialize()// Handles the initialization of blocks. Is run at the end of each loop to ensure everything is there.
        {
            inventories.Clear();// This must be done to avoid memory leaks.
            refineries.Clear();
            furnaces.Clear();
            assemblers.Clear();
            reactors.Clear();
            gasGenerators.Clear();
            gatlings.Clear();
            missileLaunchers.Clear();
            lcds.Clear();
            if (!Me.CustomName.ToLower().Contains(TAG.ToLower()))// If we have no tag at all.
            {
                Me.CustomName += " " + TAG;// Add a tag.
            }//
            else if (!Me.CustomName.Contains(TAG))// We know we have a tag, but run this when the tag isn't exactly equal.
            {
                string customName = Me.CustomName;// Replacing the incorrect tag with the proper version.
                int index = customName.ToLower().IndexOf(TAG.ToLower());
                customName = customName.Remove(index, TAG.Length);
                customName = customName.Insert(index, TAG);
                Me.CustomName = customName;
            }//
            GridTerminalSystem.SearchBlocksOfName(TAG, blocks);
            foreach (IMyTerminalBlock block in blocks)
            {
                if (!block.CustomName.Contains(TAG))// If the tag doesn't match up exactly, correct the tag.
                {
                    string customName = block.CustomName;
                    int index = customName.ToLower().IndexOf(TAG.ToLower());
                    customName = customName.Remove(index, TAG.Length);
                    customName = customName.Insert(index, TAG);
                    block.CustomName = customName;
                }//
                IMyRefinery refinery = block as IMyRefinery;// This will return null if the block isn't a refinery block.
                if (refinery != null)
                {
                    if (refinery.BlockDefinition.SubtypeId.Equals(FURNACE_TYPE_ID))// Both Refinieries and Arc Furnaces are refineries. Seperate them by subtype.
                        furnaces.Add(refinery);
                    else
                        refineries.Add(refinery);
                    continue;
                }//
                IMyAssembler assembler = block as IMyAssembler;
                if (assembler != null)
                {
                    assemblers.Add(assembler);
                    continue;
                }//
                IMyReactor reactor = block as IMyReactor;
                if (reactor != null)
                {
                    reactors.Add(reactor);
                    continue;
                }//
                IMyGasGenerator gasGenerator = block as IMyGasGenerator;
                if (gasGenerator != null)
                {
                    gasGenerators.Add(gasGenerator);
                    continue;
                }//
                IMyLargeGatlingTurret gatlingTurret = block as IMyLargeGatlingTurret;
                IMySmallGatlingGun gatlingGun = block as IMySmallGatlingGun;
                if ((gatlingTurret != null) | (gatlingGun != null))
                {
                    gatlings.Add(block);
                    continue;
                }//
                IMyLargeMissileTurret missileTurret = block as IMyLargeMissileTurret;
                IMySmallMissileLauncherReload smallLauncherReload = block as IMySmallMissileLauncherReload;
                if ((missileTurret != null) | (smallLauncherReload != null))
                {
                    missileLaunchers.Add(block);
                    continue;
                }//
                IMySmallMissileLauncher missileLauncher = block as IMySmallMissileLauncher;
                if ((missileLauncher != null) & (block.BlockDefinition.SubtypeId.Equals("LargeMissileLauncher")))
                {
                    missileLaunchers.Add(block);
                    continue;
                }//
                IMyProgrammableBlock programmableBlock = block as IMyProgrammableBlock;
                if (programmableBlock != null)
                {
                    if (!programmableBlock.Equals(Me) & programmableBlock.IsWorking)// If the programmable block isn't the one running this instance and it is working.
                    {
                        if (programmableBlock.CustomName.ToLower().Contains(CONNECTED_PB_TAG.ToLower()))// Check if it has the connected PB tag.
                        {
                            if (!programmableBlock.CustomName.Contains(CONNECTED_PB_TAG))
                            {
                                string customName = programmableBlock.CustomName;
                                int index = customName.ToLower().IndexOf(CONNECTED_PB_TAG.ToLower());
                                customName = customName.Remove(index, CONNECTED_PB_TAG.Length);
                                customName = customName.Insert(index, CONNECTED_PB_TAG);
                                programmableBlock.CustomName = customName;
                            }//
                            connectedPBs.Add(programmableBlock);
                            continue;
                        }//
                        else// Assume this PB is running the same script.
                        {
                            if (programmableBlock.CubeGrid.EntityId == Me.CubeGrid.EntityId)
                            {
                                Echo("ERROR: MORE THAN ONE IAN ON ONE GRID");
                                active = false;// Both PBs will disable themselves and show an error.
                                continue;
                            }//
                            else if (programmableBlock.CubeGrid.GridSize > Me.CubeGrid.GridSize)// The PB with the biggest grid size will be dominant.
                            {
                                active = false;
                                continue;
                            }
                            active = true;// None of the exceptions have occured, so we are free to resume functioning. This will ensure IAN plays nice with it's double.
                            continue;
                        }//
                    }//
                }//
                IMyTextPanel panel = block as IMyTextPanel;
                if (panel != null)
                    lcds.Add(panel);
            }//
        }//

        IEnumerable<bool> Render()
        {
            yield return true;
        }

        void Display()// Render and display information on Lcd screens.
        {

        }//

        struct ItemInformation
        {
            public int sortOrder;// The order in which the items will be sorted by. The lowest number is always the first slot.
            public float multiplier;//The multiplier used to increase the quota of this item.
            public double baseQuota;// The base quota of the item. The minimum amount of items the quota will maintain. Serves as the base for a multiplier.
            public bool disableMultiplier;// If set to true the quota will become static and won't apply a multiplier. (If you ever only want so much of something.)
            public bool large;// Flag that indicates whether the object can fit through small conveyors. Useful for determining what can move where.
        };

    }
}