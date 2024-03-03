using GameCode.Shared.Components;
using Unity.Burst;
using Unity.Burst.Intrinsics;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Transforms;

namespace GameCode.Server.Systems
{
    public struct OrcaLine
    {
        public float2 Direction;
        public float2 Point;
    }

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
        private const int c_cellSize = 1;
        private int _cellsAcross;

        private const float c_maxSpeed = 2.0f;
        private const float c_neighborDist = 1.2f;
        private const float c_timeHorizon = 10.0f;
        private const float c_timeHorizonObst = 10.0f;
        private const float c_timeStep = 0.25f;
        private const float RVO_EPSILON = 0.00001f;

        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<HordeUnitTag>();
            _agentsQuery = new EntityQueryBuilder(Allocator.Temp).WithAll<HordeUnitTag>().Build(ref state);
            var worldWidthInTiles = 256;
            _cellsAcross = worldWidthInTiles / c_cellSize;

            _spatialGrid = new NativeArray<int>(_cellsAcross * _cellsAcross * (c_bucketSize + 1), Allocator.Persistent);
            _activeCells = new NativeList<int>(_agentsQuery.CalculateEntityCount(), Allocator.Persistent);
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            var agentCount = _agentsQuery.CalculateEntityCount();

            var copyPositions = new NativeArray<float2>(agentCount, Allocator.TempJob);
            var copyVelocities = new NativeArray<float2>(agentCount, Allocator.TempJob);
            var copyRadius = new NativeArray<float>(agentCount, Allocator.TempJob);

            // PART 1: These three jobs all run on a separate threed, but are single threaded on that thread.
            // This is faster than spreading each job over multiple threads.

            // loop through all horde units and copy their positions to the temp array. This alignment of positions will help us later on.

            var copyJob = new CopyAgentDataJob
            {
                PositionTypeHandle = state.GetComponentTypeHandle<Position2dComponent>(true),
                VelocityTypeHandle = state.GetComponentTypeHandle<VelocityComponent>(true),
                RadiusTypeHandle = state.GetComponentTypeHandle<RadiusComponent>(true),
                CopyPositions = copyPositions,
                CopyVelocities = copyVelocities,
                CopyRadius = copyRadius
            }.Schedule(_agentsQuery, state.Dependency);

            // build up the spatial grid
            var buildSpatialGridJob = new BuildSpatialGridJob
            {
                BucketSize = c_bucketSize,
                CellsAcross = _cellsAcross,
                CellSize = c_cellSize,
                PositionTypeHandle = state.GetComponentTypeHandle<Position2dComponent>(true),
                SpatialGrid = _spatialGrid,
            }.Schedule(_agentsQuery, state.Dependency);

            // record which cells have changed
            var recordActiveCellsJob = new RecordActiveCellsJob
            {
                BucketSize = c_bucketSize,
                CellsAcross = _cellsAcross,
                CellSize = c_cellSize,
                PositionTypeHandle = state.GetComponentTypeHandle<Position2dComponent>(true),
                ActiveCells = _activeCells
            }.Schedule(_agentsQuery, state.Dependency);

            // PART 2 - Update desired velocities from flowfield

            var copyDesiredVelocities = new NativeArray<float2>(agentCount, Allocator.TempJob);

            // TODO fetch the flowfield data from flowfield generator system when it is implemented.
            var flowFieldGridSize = 50;
            var flowFieldStartPos = float2.zero;
            var flowfieldArray = new NativeArray<float2>(flowFieldGridSize * flowFieldGridSize, Allocator.TempJob);
            var random = new Random(16000);

            for (var i = 0; i < flowfieldArray.Length; i++)
            {
                flowfieldArray[i] = math.normalize(new float2(random.NextFloat(-1, 1), random.NextFloat(-1, 1)));
            }

            var updateDesiredVelocitiesJob = new UpdateDesiredVelocityJob
            {
                PositionTypeHandle = state.GetComponentTypeHandle<Position2dComponent>(true),
                DesiredVelocities = copyDesiredVelocities,
                FlowFieldGridSize = flowFieldGridSize,
                FlowField = flowfieldArray,
                FlowFieldStartPosition = flowFieldStartPos
            }.Schedule(_agentsQuery, state.Dependency);

            // - SYNC POINT - All these jobs must be completed in order for the next to run, so here is a sync point (aka a barrier).
            var barrier = JobHandle.CombineDependencies(buildSpatialGridJob, recordActiveCellsJob);
            barrier = JobHandle.CombineDependencies(barrier, copyJob, updateDesiredVelocitiesJob);
            barrier.Complete();

            // PART 3 - Do Collision avoidance and calculate the new velocities for all moving agents

            var newVelocities = new NativeArray<float2>(agentCount, Allocator.TempJob);

            // For small jobs like adding up vectors batch sizes between 32 and 128 are appropriate. 
            // For more expensive jobs a batch size of 1 is fine.Small batch sizes will ensure a more even distribution between worker threads.

            var batchSize = 1;

            var collisionAvoidanceJob = new CollisionAvoidanceJob
            {
                BucketSize = c_bucketSize,
                CellsAcross = _cellsAcross,
                CellSize = c_cellSize,
                TimeHorizon = c_timeHorizon,
                TimeHorizonObstacle = c_timeHorizonObst,
                TimeStep = c_timeStep,
                MaxSpeed = c_maxSpeed,
                RVO_EPSILON = RVO_EPSILON,
                Positions = copyPositions,
                DesiredVelocities = copyDesiredVelocities,
                Radiuses = copyRadius,
                Velocities = copyVelocities,
                NewVelocities = newVelocities,
                SpatialGrid = _spatialGrid
            }.Schedule(copyPositions.Length, batchSize, barrier);

            // - SYNC POINT - NewVelocities must be populated before continuing the simulation

            collisionAvoidanceJob.Complete();

            // PART 4 - Update positions of all agents based on new velocity from collision avoidance step

            var updatePositionsJob = new UpdatePositionsJob()
            {
                CopyPositions = copyPositions,
                PositionTypeHandle = state.GetComponentTypeHandle<Position2dComponent>(),
                VelocityTypeHandle = state.GetComponentTypeHandle<VelocityComponent>(),
                LocalTransformTypeHandle = state.GetComponentTypeHandle<LocalTransform>(),
                NewVelocities = newVelocities
            }.Schedule(_agentsQuery, collisionAvoidanceJob);
            
            updatePositionsJob.Complete();
            
            // PART 5 - Clean up
            var clearGridCountersJob = new ClearGridCountersJob
            {
                ActiveCells = _activeCells,
                Grid = _spatialGrid,
            }.Schedule(updatePositionsJob);

            var clearChangesJob = new ClearChangesListJob
            {
                ActiveCells = _activeCells,
            }.Schedule(clearGridCountersJob);

            var copyPositionsDisposeHandle = copyPositions.Dispose(clearChangesJob);
            var copyDesiredVelocitiesDisposeHandle = copyDesiredVelocities.Dispose(clearChangesJob);
            var copydVelocitiesDisposeHandle = copyVelocities.Dispose(clearChangesJob);
            var copyNewVelocitiesDisposeHandle = newVelocities.Dispose(clearChangesJob);
            var copyRadiusesDisposeHandle = copyRadius.Dispose(clearChangesJob);
            
            var posAndRadiusDeps = JobHandle.CombineDependencies(copyPositionsDisposeHandle, copyRadiusesDisposeHandle);

            var velocitiesDeps = JobHandle.CombineDependencies(
                copyDesiredVelocitiesDisposeHandle, 
                copydVelocitiesDisposeHandle, 
                copyNewVelocitiesDisposeHandle
            );

            var finalDependency = JobHandle.CombineDependencies(
                posAndRadiusDeps,
                velocitiesDeps
            );

            state.Dependency = finalDependency;
        }

        [BurstCompile]
        public void OnDestroy(ref SystemState state)
        {
            _spatialGrid.Dispose();
            _activeCells.Dispose();
        }
    }

    [BurstCompile]
    struct CopyAgentDataJob : IJobChunk
    {
        [ReadOnly] public ComponentTypeHandle<Position2dComponent> PositionTypeHandle;
        [ReadOnly] public ComponentTypeHandle<VelocityComponent> VelocityTypeHandle;
        [ReadOnly] public ComponentTypeHandle<RadiusComponent> RadiusTypeHandle;

        [WriteOnly] public NativeArray<float2> CopyPositions;
        [WriteOnly] public NativeArray<float2> CopyVelocities;
        [WriteOnly] public NativeArray<float> CopyRadius;

        [BurstCompile]
        public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask,
            in v128 chunkEnabledMask)
        {
            var positions = chunk.GetNativeArray(ref PositionTypeHandle);
            var velocities = chunk.GetNativeArray(ref VelocityTypeHandle);
            var radius = chunk.GetNativeArray(ref RadiusTypeHandle);

            var enumerator = new ChunkEntityEnumerator(useEnabledMask, chunkEnabledMask, chunk.Count);

            while (enumerator.NextEntityIndex(out var i))
            {
                CopyPositions[i] = positions[i].Position;
                CopyVelocities[i] = velocities[i].Velocity;
                CopyRadius[i] = radius[i].Radius;
            }
        }
    }

    [BurstCompile]
    struct BuildSpatialGridJob : IJobChunk
    {
        [ReadOnly] public int BucketSize;
        [ReadOnly] public int CellsAcross;
        [ReadOnly] public int CellSize;

        [ReadOnly] public ComponentTypeHandle<Position2dComponent> PositionTypeHandle;

        public NativeArray<int> SpatialGrid;

        [BurstCompile]
        public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask,
            in v128 chunkEnabledMask)
        {
            var positions = chunk.GetNativeArray(ref PositionTypeHandle);

            var enumerator = new ChunkEntityEnumerator(useEnabledMask, chunkEnabledMask, chunk.Count);

            while (enumerator.NextEntityIndex(out var i))
            {
                float px = positions[i].Position.x;
                float py = positions[i].Position.y;

                int hash = (int)(math.floor(px / CellSize) + math.floor((py / CellSize)) * CellsAcross);
                int gridIndex = hash * BucketSize;

                int count = SpatialGrid[gridIndex];
                int cellIndex = gridIndex + 1;

                // the grid array is structured like this: (index 0 is the unit count of the cell, and the next 4 indices are 
                // the array indice of the temp copy positions array)
                // So we can put the entity index into the array based on the current cell's unit count
                if (count < BucketSize - 1)
                {
                    // if cell isnt full then add current entity to the cell
                    SpatialGrid[cellIndex + count] = i;
                    // and increase cell count
                    SpatialGrid[gridIndex] = count + 1;
                }
            }
        }
    }

    // This job's purpose is solely for cleanup
    // If the map is large, clearing the grid array every frame takes very long
    // Simply recording the active cells, and then clearing just those can be 100x + faster!
    // The reason this is a separate job from build map job is just so we can do it on another thread,
    // Even tho it seems like we are doing the same work twice, its actually faster as its split into 2 threads
    [BurstCompile]
    struct RecordActiveCellsJob : IJobChunk
    {
        [ReadOnly] public int BucketSize;
        [ReadOnly] public int CellsAcross;
        [ReadOnly] public int CellSize;

        [ReadOnly] public ComponentTypeHandle<Position2dComponent> PositionTypeHandle;

        [WriteOnly] public NativeList<int> ActiveCells;

        [BurstCompile]
        public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask,
            in v128 chunkEnabledMask)
        {
            var positions = chunk.GetNativeArray(ref PositionTypeHandle);

            var enumerator = new ChunkEntityEnumerator(useEnabledMask, chunkEnabledMask, chunk.Count);

            while (enumerator.NextEntityIndex(out var i))
            {
                float px = positions[i].Position.x;
                float py = positions[i].Position.y;

                int hash = (int)(math.floor(px / CellSize) + math.floor((py / CellSize)) * CellsAcross);

                ActiveCells.Add(hash * BucketSize);
            }
        }
    }

    // Updates the desired velocities of all agents to underlying flowfield vector (if one exists)
    [BurstCompile]
    struct UpdateDesiredVelocityJob : IJobChunk
    {
        [ReadOnly] public ComponentTypeHandle<Position2dComponent> PositionTypeHandle;
        [ReadOnly] public float2 FlowFieldStartPosition;
        [ReadOnly] public int FlowFieldGridSize;
        [ReadOnly] public NativeArray<float2> FlowField;
        [WriteOnly] public NativeArray<float2> DesiredVelocities;

        [BurstCompile]
        public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask,
            in v128 chunkEnabledMask)
        {
            var positions = chunk.GetNativeArray(ref PositionTypeHandle);

            var enumerator = new ChunkEntityEnumerator(useEnabledMask, chunkEnabledMask, chunk.Count);

            while (enumerator.NextEntityIndex(out var i))
            {
                float worldPositionX = positions[i].Position.x;
                float worldPositionY = positions[i].Position.y;

                int tileX = (int)worldPositionX - (int)FlowFieldStartPosition.x;
                int tileY = (int)worldPositionY - (int)FlowFieldStartPosition.y;
                float2 flowFieldVector =
                    tileX < 0 || tileY < 0 || tileX >= FlowFieldGridSize || tileY >= FlowFieldGridSize
                        ? float2.zero
                        : FlowField[tileX + tileY * FlowFieldGridSize];
                DesiredVelocities[i] = flowFieldVector;
            }
        }
    }

    [BurstCompile]
    struct UpdatePositionsJob : IJobChunk
    {
        public ComponentTypeHandle<LocalTransform> LocalTransformTypeHandle;
        public ComponentTypeHandle<Position2dComponent> PositionTypeHandle;
        public ComponentTypeHandle<VelocityComponent> VelocityTypeHandle;
        
        [ReadOnly] public NativeArray<float2> CopyPositions;
        [ReadOnly] public NativeArray<float2> NewVelocities;
        
        [BurstCompile]
        public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
        {
            var positions = chunk.GetNativeArray(ref PositionTypeHandle);
            var velocities = chunk.GetNativeArray(ref VelocityTypeHandle);
            var transforms = chunk.GetNativeArray(ref LocalTransformTypeHandle);

            var enumerator = new ChunkEntityEnumerator(useEnabledMask, chunkEnabledMask, chunk.Count);

            while (enumerator.NextEntityIndex(out var i))
            {
                transforms[i] = new LocalTransform
                {
                    Position = new float3(CopyPositions[i].x, 0, CopyPositions[i].y),
                    Rotation = transforms[i].Rotation,
                    Scale = transforms[i].Scale
                };

                positions[i] = new Position2dComponent
                {
                    Position = CopyPositions[i]
                };
                
                velocities[i] = new VelocityComponent
                {
                    Velocity = NewVelocities[i]
                };
            }
        }
    }
    
    [BurstCompile]
    struct ClearGridCountersJob : IJob
    {
        [ReadOnly] public NativeList<int> ActiveCells;
        public NativeArray<int> Grid;

        [BurstCompile]
        public void Execute()
        {
            for (var i = 0; i < ActiveCells.Length - 8; i += 8)
            {
                Grid[i] = 0;
                Grid[i+1] = 0;
                Grid[i+2] = 0;
                Grid[i+3] = 0;
                Grid[i+4] = 0;
                Grid[i+5] = 0;
                Grid[i+6] = 0;
                Grid[i+7] = 0;
            }

            for (var i = 0; i < ActiveCells.Length; i++)
            {
                Grid[ActiveCells[i]] = 0;
            }
        }
    }

    [BurstCompile]
    struct ClearChangesListJob : IJob
    {
        public NativeList<int> ActiveCells;

        [BurstCompile]
        public void Execute()
        {
            ActiveCells.Clear();
        }
    }

    [BurstCompile]
    struct CollisionAvoidanceJob : IJobParallelFor
    {
        [ReadOnly] public int BucketSize;
        [ReadOnly] public int CellsAcross;
        [ReadOnly] public int CellSize;

        [ReadOnly] public float TimeHorizon;
        [ReadOnly] public float TimeHorizonObstacle;
        [ReadOnly] public float TimeStep;
        [ReadOnly] public float MaxSpeed;
        [ReadOnly] public float RVO_EPSILON;

        [ReadOnly, NativeDisableParallelForRestriction]
        public NativeArray<float2> Positions;

        [ReadOnly, NativeDisableParallelForRestriction]
        public NativeArray<float2> DesiredVelocities;

        [ReadOnly, NativeDisableParallelForRestriction]
        public NativeArray<float2> Velocities;

        [ReadOnly, NativeDisableParallelForRestriction]
        public NativeArray<float> Radiuses;

        [ReadOnly] public NativeArray<int> SpatialGrid;

        [NativeDisableParallelForRestriction] public NativeArray<float2> NewVelocities;

        [BurstCompile]
        public void Execute(int index)
        {
            float2 agentPosition = Positions[index];
            float2 agentDesirecVelocity = DesiredVelocities[index];
            float2 agentVelocity = Velocities[index];
            float agentRadius = Radiuses[index];

            float px = agentPosition.x;
            float py = agentPosition.y;

            int cellHash = (int)(math.floor(px / CellSize) + math.floor(py / CellSize) * CellsAcross);
            int xR = (int)math.round(px);
            int yR = (int)math.round(py);
            int xD = math.select(1, -1, xR < px);
            int yD = math.select(1, -1, yR < py);

            var cellsToCheck = new NativeArray<int>(4, Allocator.Temp);
            cellsToCheck[0] = cellHash;
            var cellsToCheckCounter = 1;

            bool xOffset = math.abs(xR - px) < 0.3f;
            bool yOffset = math.abs(yR - py) < 0.3f;

            if (xOffset)
                cellsToCheck[cellsToCheckCounter++] = cellHash + xD;
            if (yOffset)
                cellsToCheck[cellsToCheckCounter++] = cellHash + yD * CellsAcross;
            if (xOffset && yOffset)
                cellsToCheck[cellsToCheckCounter++] = cellHash + yD * CellsAcross;

            var orcaLines = new NativeList<OrcaLine>(Allocator.Temp);
            
            // @TODO(Anders): process obstacles collision checks here
            
            int numObstacleLines = orcaLines.Length;

            float invTimeHorizon = 1.0f / TimeHorizon;

            for (int i = 0; i < cellsToCheckCounter; i++)
            {
                int gridIndex = cellsToCheck[i] * BucketSize;
                int count = SpatialGrid[gridIndex];
                int cellIndex = gridIndex + 1;

                for (int j = 1; j < count; j++)
                {
                    int otherIndex = SpatialGrid[cellIndex + j];
                    float2 otherPosition = Positions[otherIndex];
                    float2 otherVelocity = Velocities[otherIndex];
                    float otherRadius = Radiuses[otherIndex];

                    float2 relativePosition = otherPosition - agentPosition;
                    float2 relativeVelocity = agentVelocity - otherVelocity;
                    float distSq = math.distancesq(relativePosition.x, relativePosition.y);
                    float combinedRadius = agentRadius + otherRadius;
                    float combinedRadiusSq = math.sqrt(combinedRadius);

                    OrcaLine line;
                    float2 u;

                    if (distSq > combinedRadiusSq)
                    {
                        float2 w = relativeVelocity - invTimeHorizon * relativePosition;

                        /* Vector from cutoff center to relative velocity. */
                        float wLengthSq = AbsSq(w);
                        float dotProduct1 = math.dot(w, relativePosition);

                        if (dotProduct1 < 0.0f && Sqr(dotProduct1) > combinedRadiusSq * wLengthSq)
                        {
                            /* Project on cut-off circle. */
                            float wLength = math.sqrt(wLengthSq);
                            float2 unitW = w / wLength;

                            line.Direction = new float2(unitW.y, -unitW.x);
                            u = (combinedRadius * invTimeHorizon - wLength) * unitW;
                        }
                        else
                        {
                            /* Project on legs. */
                            float leg = math.sqrt(distSq - combinedRadiusSq);

                            if (Det(relativePosition, w) > 0.0f)
                            {
                                /* Project on left leg. */
                                line.Direction = new float2(
                                    relativePosition.x * leg - relativePosition.y * combinedRadius,
                                    relativePosition.x * combinedRadius + relativePosition.y * leg) / distSq;
                            }
                            else
                            {
                                /* Project on right leg. */
                                line.Direction = -new float2(
                                    relativePosition.x * leg + relativePosition.y * combinedRadius,
                                    -relativePosition.x * combinedRadius + relativePosition.y * leg) / distSq;
                            }

                            float dotProduct2 = math.dot(relativeVelocity, line.Direction);
                            u = dotProduct2 * line.Direction - relativeVelocity;
                        }
                    }
                    else
                    {
                        /* Collision. Project on cut-off circle of time timeStep. */
                        float invTimeStep = 1.0f / TimeStep;

                        /* Vector from cutoff center to relative velocity. */
                        float2 w = relativeVelocity - invTimeStep * relativePosition;

                        float wLength = Abs(w);
                        float2 unitW = w / wLength;

                        line.Direction = new float2(unitW.y, -unitW.x);
                        u = (combinedRadius * invTimeStep - wLength) * unitW;
                    }

                    line.Point = agentVelocity + 0.5f * u;
                    orcaLines.Add(line);
                }

                float2 newVelocity = default;
                int lineFail = LinearProgram2(ref orcaLines, MaxSpeed, agentDesirecVelocity, false, ref newVelocity);

                if (lineFail < orcaLines.Length)
                {
                    LinearProgram3(ref orcaLines, numObstacleLines, lineFail, MaxSpeed, ref newVelocity);
                }

                NewVelocities[index] = newVelocity;
            }

            cellsToCheck.Dispose();
            orcaLines.Dispose();
        }

        bool LinearProgram1(ref NativeList<OrcaLine> lines, int lineNo, float radius, float2 optVelocity,
            bool directionOpt, ref float2 result)
        {
            float dotProduct = math.dot(lines[lineNo].Point, lines[lineNo].Direction);
            float discriminant = Sqr(dotProduct) + Sqr(radius) - AbsSq(lines[lineNo].Point);

            if (discriminant < 0.0f)
            {
                /* Max speed circle fully invalidates line lineNo. */
                return false;
            }

            float sqrtDiscriminant = math.sqrt(discriminant);
            float tLeft = -dotProduct - sqrtDiscriminant;
            float tRight = -dotProduct + sqrtDiscriminant;

            for (int i = 0; i < lineNo; ++i)
            {
                float denominator = Det(lines[lineNo].Direction, lines[i].Direction);
                float numerator = Det(lines[i].Direction, lines[lineNo].Point - lines[i].Point);

                if (FAbs(denominator) <= RVO_EPSILON)
                {
                    /* Lines lineNo and i are (almost) parallel. */
                    if (numerator < 0.0f)
                    {
                        return false;
                    }

                    continue;
                }

                float t = numerator / denominator;

                if (denominator >= 0.0f)
                {
                    /* Line i bounds line lineNo on the right. */
                    tRight = math.min(tRight, t);
                }
                else
                {
                    /* Line i bounds line lineNo on the left. */
                    tLeft = math.max(tLeft, t);
                }

                if (tLeft > tRight)
                {
                    return false;
                }
            }

            if (directionOpt)
            {
                /* Optimize direction. */
                if (math.dot(optVelocity, lines[lineNo].Direction) > 0.0f)
                {
                    /* Take right extreme. */
                    result = lines[lineNo].Point + tRight * lines[lineNo].Direction;
                }
                else
                {
                    /* Take left extreme. */
                    result = lines[lineNo].Point + tLeft * lines[lineNo].Direction;
                }
            }
            else
            {
                /* Optimize closest point. */
                float t = math.dot(lines[lineNo].Direction, optVelocity - lines[lineNo].Point);

                if (t < tLeft)
                {
                    result = lines[lineNo].Point + tLeft * lines[lineNo].Direction;
                }
                else if (t > tRight)
                {
                    result = lines[lineNo].Point + tRight * lines[lineNo].Direction;
                }
                else
                {
                    result = lines[lineNo].Point + t * lines[lineNo].Direction;
                }
            }

            return true;
        }

        int LinearProgram2(ref NativeList<OrcaLine> lines, float radius, float2 optVelocity, bool directionOpt,
            ref float2 result)
        {
            if (directionOpt)
            {
                /*
                 * Optimize direction. Note that the optimization velocity is of
                 * unit length in this case.
                 */
                result = optVelocity * radius;
            }
            else if (AbsSq(optVelocity) > Sqr(radius))
            {
                /* Optimize closest point and outside circle. */
                result = math.normalize(optVelocity) * radius;
            }
            else
            {
                /* Optimize closest point and inside circle. */
                result = optVelocity;
            }

            for (int i = 0; i < lines.Length; ++i)
            {
                if (Det(lines[i].Direction, lines[i].Point - result) > 0.0f)
                {
                    /* Result does not satisfy constraint i. Compute new optimal result. */
                    float2 tempResult = result;
                    if (!LinearProgram1(ref lines, i, radius, optVelocity, directionOpt, ref result))
                    {
                        result = tempResult;

                        return i;
                    }
                }
            }

            return lines.Length;
        }

        void LinearProgram3(ref NativeList<OrcaLine> lines, int numObstLines, int beginLine, float radius,
            ref float2 result)
        {
            float distance = 0.0f;

            for (int i = beginLine; i < lines.Length; ++i)
            {
                if (Det(lines[i].Direction, lines[i].Point - result) > distance)
                {
                    /* Result does not satisfy constraint of line i. */
                    var projLines = new NativeList<OrcaLine>(Allocator.Temp);
                    for (int ii = 0; ii < numObstLines; ++ii)
                    {
                        projLines.Add(lines[ii]);
                    }

                    for (int j = numObstLines; j < i; ++j)
                    {
                        OrcaLine line;

                        float determinant = Det(lines[i].Direction, lines[j].Direction);

                        if (FAbs(determinant) <= RVO_EPSILON)
                        {
                            /* Line i and line j are parallel. */
                            if (math.dot(lines[i].Direction, lines[j].Direction) > 0.0f)
                            {
                                /* Line i and line j point in the same direction. */
                                continue;
                            }
                            else
                            {
                                /* Line i and line j point in opposite direction. */
                                line.Point = 0.5f * (lines[i].Point + lines[j].Point);
                            }
                        }
                        else
                        {
                            line.Point = lines[i].Point +
                                         (Det(lines[j].Direction, lines[i].Point - lines[j].Point) / determinant) *
                                         lines[i].Direction;
                        }

                        line.Direction = math.normalize(lines[j].Direction - lines[i].Direction);
                        projLines.Add(line);
                    }

                    float2 tempResult = result;
                    if (LinearProgram2(ref projLines, radius, new float2(-lines[i].Direction.y, lines[i].Direction.x),
                            true, ref result) < projLines.Length)
                    {
                        /*
                         * This should in principle not happen. The result is by
                         * definition already in the feasible region of this
                         * linear program. If it fails, it is due to small
                         * floating point error, and the current result is kept.
                         */
                        result = tempResult;
                    }

                    distance = Det(lines[i].Direction, lines[i].Point - result);

                    projLines.Dispose();
                }
            }
        }

        float Abs(float2 vec)
        {
            return math.sqrt(AbsSq(vec));
        }

        float AbsSq(float2 vec)
        {
            return math.distancesq(vec.x, vec.y);
        }

        float Det(float2 vec1, float2 vec2)
        {
            return vec1.x * vec2.y - vec1.y * vec2.x;
        }

        float DistSqPointLineSegment(float2 vec1, float2 vec2, float2 vec3)
        {
            var v1 = vec3 - vec1;
            var v2 = vec2 - vec1;
            float r = (math.dot(v1, v2)) / AbsSq(vec2 - vec1);

            if (r < 0.0f)
                return AbsSq(vec3 - vec1);

            if (r > 1.0f)
                return AbsSq(vec3 - vec2);

            return AbsSq(vec3 - (vec1 + r * (vec2 - vec1)));
        }

        float FAbs(float scalar)
        {
            return math.abs(scalar);
        }

        float LeftOf(float2 a, float2 b, float2 c)
        {
            return Det(a - c, b - a);
        }

        float Sqr(float scalar)
        {
            return scalar * scalar;
        }
    }
}