﻿/* AIPath
 * 
 * Contains a processed version of the MSTS PAT file.
 * The processing saves information needed for AI train dispatching and to align switches.
 * Could this be used for player trains also?
 * 
/// COPYRIGHT 2009 by the Open Rails project.
/// This code is provided to enable you to contribute improvements to the open rails program.  
/// Use of the code for any other purpose or distribution of the code to anyone else
/// is prohibited without specific written permission from admin@openrails.org.
 */
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;
using Microsoft.Xna.Framework;
using MSTS;

namespace ORTS
{
    public enum AIPathNodeType { Other, Stop, SidingStart, SidingEnd, Couple, Uncouple, Reverse };

    public class AIPath
    {
        public TrackDB TrackDB;
        public TSectionDatFile TSectionDat;
        public AIPathNode FirstNode;    // path starting node

        /// <summary>
        /// Creates an AIPath from PAT file information.
        /// First creates all the nodes and then links them together into a main list
        /// with optional parallel siding list.
        /// </summary>
        public AIPath(PATFile patFile, TDBFile TDB, TSectionDatFile tsectiondat)
        {
            TrackDB = TDB.TrackDB;
            TSectionDat = tsectiondat;
            List<AIPathNode> nodes = new List<AIPathNode>();
            foreach (TrPathNode tpn in patFile.TrPathNodes)
                nodes.Add(new AIPathNode(tpn, patFile.TrackPDPs[(int)tpn.FromPDP], TrackDB));
            FirstNode = nodes[0];
            for (int i = 0; i < nodes.Count; i++)
            {
                AIPathNode node = nodes[i];
                TrPathNode tpn = patFile.TrPathNodes[i];
                if (tpn.NextNode != 0xffffffff)
                {
                    node.NextMainNode = nodes[(int)tpn.NextNode];
                    node.NextMainTVNIndex = node.FindTVNIndex(node.NextMainNode, TDB, tsectiondat);
                    if (node.JunctionIndex >= 0)
                        node.IsFacingPoint = TestFacingPoint(node.JunctionIndex, node.NextMainTVNIndex);
                }
                if (tpn.C != 0xffffffff)
                {
                    node.NextSidingNode = nodes[(int)tpn.C];
                    node.NextSidingTVNIndex = node.FindTVNIndex(node.NextSidingNode, TDB, tsectiondat);
                    if (node.JunctionIndex >= 0)
                        node.IsFacingPoint = TestFacingPoint(node.JunctionIndex, node.NextSidingTVNIndex);
                }
                if (node.NextMainNode != null && node.NextSidingNode != null)
                    node.Type = AIPathNodeType.SidingStart;
            }
            for (AIPathNode node1 = FirstNode; node1 != null; node1 = node1.NextMainNode)
            {
                //Console.WriteLine("path {0} {1} {2 } {3 } {4}", node1.ID, node1.Type, node1.JunctionIndex, node1.NextMainTVNIndex, node1.NextSidingTVNIndex);
                AIPathNode node2 = node1.NextSidingNode;
                while (node2 != null && node2.NextSidingNode != null)
                {
                    //Console.WriteLine("siding {0} {1} {2} {3} {4}", node2.ID, node2.Type, node2.JunctionIndex, node2.NextMainTVNIndex, node2.NextSidingTVNIndex);
                    node2 = node2.NextSidingNode;
                }
                if (node2 != null)
                    node2.Type = AIPathNodeType.SidingEnd;
            }
        }

        /// <summary>
        /// Aligns the switch for the specified juction node so that the specified
        /// vector node will be used as the selected route.
        /// </summary>
        public void AlignSwitch(int junctionIndex, int vectorIndex)
        {
            //Console.WriteLine("align {0} {1}", junctionIndex, vectorIndex);
            if (junctionIndex < 0 || vectorIndex < 0)
                return;
            TrackNode tn = TrackDB.TrackNodes[junctionIndex];
            if (tn.TrJunctionNode == null || tn.TrPins[0].Link == vectorIndex)
                return;
            tn.TrJunctionNode.SelectedRoute = tn.TrPins[1].Link == vectorIndex ? 0 : 1;
        }

        /// <summary>
        /// returns true if the switch for the specified juction node is aligned
        /// so that the specified vector node will be used as the selected route.
        /// </summary>
        public bool SwitchIsAligned(int junctionIndex, int vectorIndex)
        {
            if (junctionIndex < 0 || vectorIndex < 0)
                return true;
            TrackNode tn = TrackDB.TrackNodes[junctionIndex];
            if (tn.TrJunctionNode == null || tn.TrPins[0].Link == vectorIndex)
                return true;
            return tn.TrJunctionNode.SelectedRoute == (tn.TrPins[1].Link == vectorIndex ? 0 : 1);
        }

        /// <summary>
        /// returns true if the specified vector node is at the facing point end of
        /// the specified juction node, else false.
        /// </summary>
        public bool TestFacingPoint(int junctionIndex, int vectorIndex)
        {
            if (junctionIndex < 0 || vectorIndex < 0)
                return false;
            TrackNode tn = TrackDB.TrackNodes[junctionIndex];
            if (tn.TrJunctionNode == null || tn.TrPins[0].Link == vectorIndex)
                return false;
            return true;
        }

        /// <summary>
        /// finds the first path node after start that refers to the specified track node.
        /// </summary>
        public AIPathNode FindTrackNode(AIPathNode start, int trackNodeIndex)
        {
            for (AIPathNode node = start; node != null; node = node.NextMainNode)
            {
                if (node.NextMainTVNIndex == trackNodeIndex || node.NextSidingTVNIndex == trackNodeIndex)
                    return node;
                for (AIPathNode node1 = node.NextSidingNode; node1 != null; node1 = node1.NextSidingNode)
                    if (node.NextMainTVNIndex == trackNodeIndex || node.NextSidingTVNIndex == trackNodeIndex)
                        return node1;
            }
            return null;
        }
    }

    public class AIPathNode
    {
        public int ID;
        public AIPathNodeType Type = AIPathNodeType.Other;
        public int WaitTimeS = 0;   // number of seconds to wait after stopping at this node
        public int NCars = 0;       // number of cars to uncouple, negative means keep rear
        public AIPathNode NextMainNode = null;      // next path node on main path
        public AIPathNode NextSidingNode = null;    // next path node on siding path
        public int NextMainTVNIndex = -1;   // index of main vector node leaving this path node
        public int NextSidingTVNIndex = -1; // index of siding vector node leaving this path node
        public WorldLocation Location;      // coordinates for this path node
        public int JunctionIndex = -1;      // index of junction node, -1 if none
        public bool IsFacingPoint = false;// true if this node entered from the facing point end

        /// <summary>
        /// Creates a single AIPathNode and initializes everything that do not depend on other nodes.
        /// The AIPath constructor will initialize the rest.
        /// </summary>
        public AIPathNode(TrPathNode tpn, TrackPDP pdp, TrackDB trackDB)
        {
            ID = (int)tpn.FromPDP;
            if ((tpn.A & 01) != 0)
                Type = AIPathNodeType.Reverse;
            else if ((tpn.A & 02) != 0)
            {
                Type = AIPathNodeType.Stop;
                WaitTimeS = (int)((tpn.A >> 16) & 0xffff);
                if (WaitTimeS >= 40000 && WaitTimeS < 60000)
                {
                    Type = AIPathNodeType.Uncouple;
                    NCars = (WaitTimeS / 100) % 100;
                    if (WaitTimeS >= 50000)
                        NCars = -NCars;
                    WaitTimeS %= 100;
                }
                else if (WaitTimeS >= 60000)
                {
                    Type = AIPathNodeType.Couple;
                    WaitTimeS %= 1000;
                }
            }
            Location = new WorldLocation(pdp.TileX, pdp.TileZ, pdp.X, pdp.Y, pdp.Z);
            if (pdp.A == 2)
            {
                for (int j = 0; j < trackDB.TrackNodes.Count(); j++)
                {
                    TrackNode tn = trackDB.TrackNodes[j];
                    if (tn != null && tn.TrJunctionNode != null && tn.UiD.WorldTileX == pdp.TileX && tn.UiD.WorldTileZ == pdp.TileZ)
                    {
                        float dx = tn.UiD.X - pdp.X;
                        dx += (tn.UiD.TileX - pdp.TileX) * 2048;
                        float dz = tn.UiD.Z - pdp.Z;
                        dz += (tn.UiD.TileZ - pdp.TileZ) * 2048;
                        float dy = tn.UiD.Y - pdp.Y;
                        if (Math.Abs(dx) + Math.Abs(dz) + Math.Abs(dy) < 0.1)  // we found it at this junction
                        {
                            JunctionIndex = j;
                            break;
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Returns the index of the vector node connection this path node to another.
        /// </summary>
        public int FindTVNIndex(AIPathNode nextNode, TDBFile TDB, TSectionDatFile tsectiondat)
        {
            if (JunctionIndex < 0)
            {
                TDBTraveller traveller = new TDBTraveller(Location.TileX, Location.TileZ, Location.Location.X, Location.Location.Z, 0, TDB, tsectiondat);
                return traveller.TrackNodeIndex;
            }
            else if (nextNode.JunctionIndex < 0)
            {
                TDBTraveller traveller = new TDBTraveller(nextNode.Location.TileX, nextNode.Location.TileZ, nextNode.Location.Location.X, nextNode.Location.Location.Z, 0, TDB, tsectiondat);
                return traveller.TrackNodeIndex;
            }
            else
            {
                for (int i = 0; i < TDB.TrackDB.TrackNodes.Count(); i++)
                {
                    TrackNode tn = TDB.TrackDB.TrackNodes[i];
                    if (tn == null || tn.TrVectorNode == null)
                        continue;
                    if (tn.TrPins[0].Link == JunctionIndex && tn.TrPins[1].Link == nextNode.JunctionIndex)
                        return i;
                    if (tn.TrPins[1].Link == JunctionIndex && tn.TrPins[0].Link == nextNode.JunctionIndex)
                        return i;
                }
            }
            return -1;
        }
    }
}
