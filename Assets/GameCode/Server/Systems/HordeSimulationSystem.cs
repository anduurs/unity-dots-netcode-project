using GameCode.Shared.Components;
using Unity.Collections;
using Unity.Entities;

namespace GameCode.Server.Systems
{
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation)]
    public partial struct HordeSimulationSystem : ISystem
    {
        // Represents a 2d simulation region grid where first item is the number of entities in the current cell, 
        // and the next 4 are INDICES into a temp array created every frame
        private NativeArray<int> _spatialGrid;
        
        // a list of "active cells" which is cleared and refilled every frame
        private NativeList<int> _activeCells;
        
        private EntityQuery _agentsQuery;
        
        // The bucket size is how many entities are allowed to be in on cell at a time. More allowed will mean more collisions checks, at reduced performance
        // Realistically, if you set the cell size to 1x1 meter, you wont ever have more than 4 people standing in that area in an rts, and if they are,
        // it will be too crowded to see. 
        // The cell size is the dimensions of the cell in world units, and the cells across is the length of one side of your map
        private const int c_bucketSize = 8;
        private const float c_cellSize = 1f;
        private int _cellsAcross;

        private const float c_maxSpeed = 2.0f;
        private const float c_neighborDist = 1.2f;
        private const float c_timeHorizon = 10.0f;
        private const float c_timeHorizonObst = 10.0f;
        private const float c_timeStep = 0.25f;
        private const float RVO_EPSILON = 0.00001f;
        
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<HordeUnitTag>();
            _agentsQuery = state.GetEntityQuery(typeof(HordeUnitTag));
        }

        public void OnUpdate(ref SystemState state)
        {
           
        }
    }
}