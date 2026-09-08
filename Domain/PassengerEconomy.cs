using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;

namespace BDVM.Domain;

public enum PassengerContractState { Offered, Reserved, Active, CompletionPending, Completed, Cancelled }

[DataContract]
public sealed class PassengerRouteDemand
{
    [DataMember(Name = "routeId", Order = 1)] public string RouteId { get; set; } = "";
    [DataMember(Name = "originId", Order = 2)] public string OriginId { get; set; } = "";
    [DataMember(Name = "destinationId", Order = 3)] public string DestinationId { get; set; } = "";
    [DataMember(Name = "demandUnits", Order = 4)] public int DemandUnits { get; set; }
    [DataMember(Name = "maximumDemandUnits", Order = 5)] public int MaximumDemandUnits { get; set; }
    [DataMember(Name = "demandPerInterval", Order = 6)] public int DemandPerInterval { get; set; }
    [DataMember(Name = "desiredFrequencyTicks", Order = 7)] public long DesiredFrequencyTicks { get; set; }
    [DataMember(Name = "baseFarePerPassenger", Order = 8)] public long BaseFarePerPassenger { get; set; }
    [DataMember(Name = "latePenaltyPerTick", Order = 9)] public long LatePenaltyPerTick { get; set; }
    [DataMember(Name = "demandUpdatedTick", Order = 10)] public long DemandUpdatedTick { get; set; }
    [DataMember(Name = "lastCompletedServiceTick", Order = 11)] public long? LastCompletedServiceTick { get; set; }
    [DataMember(Name = "completedServices", Order = 12)] public int CompletedServices { get; set; }
    [DataMember(Name = "transportedPassengers", Order = 13)] public long TransportedPassengers { get; set; }
    [DataMember(Name = "punctualityBasisPoints", Order = 14)] public int PunctualityBasisPoints { get; set; } = 10000;
    [DataMember(Name = "version", Order = 15)] public long Version { get; set; }
}

[DataContract]
public sealed class PassengerServiceContract
{
    [DataMember(Name = "contractId", Order = 1)] public string ContractId { get; set; } = "";
    [DataMember(Name = "routeId", Order = 2)] public string RouteId { get; set; } = "";
    [DataMember(Name = "passengerJobId", Order = 3)] public string PassengerJobId { get; set; } = "";
    [DataMember(Name = "assignmentId", Order = 4)] public string AssignmentId { get; set; } = "";
    [DataMember(Name = "assetIds", Order = 5)] public List<string> AssetIds { get; set; } = new List<string>();
    [DataMember(Name = "operator", Order = 6)] public AssetOwnerRef Operator { get; set; } = new AssetOwnerRef();
    [DataMember(Name = "capacity", Order = 7)] public int Capacity { get; set; }
    [DataMember(Name = "bookedPassengers", Order = 8)] public int BookedPassengers { get; set; }
    [DataMember(Name = "scheduledDepartureTick", Order = 9)] public long ScheduledDepartureTick { get; set; }
    [DataMember(Name = "scheduledArrivalTick", Order = 10)] public long ScheduledArrivalTick { get; set; }
    [DataMember(Name = "actualDepartureTick", Order = 11)] public long? ActualDepartureTick { get; set; }
    [DataMember(Name = "actualArrivalTick", Order = 12)] public long? ActualArrivalTick { get; set; }
    [DataMember(Name = "maximumQuotedRevenue", Order = 13)] public long MaximumQuotedRevenue { get; set; }
    [DataMember(Name = "observedVanillaRevenue", Order = 14)] public long ObservedVanillaRevenue { get; set; }
    [DataMember(Name = "punctualityPenalty", Order = 15)] public long PunctualityPenalty { get; set; }
    [DataMember(Name = "paidRevenue", Order = 16)] public long PaidRevenue { get; set; }
    [DataMember(Name = "financialAdjustmentApplied", Order = 17)] public bool FinancialAdjustmentApplied { get; set; }
    [DataMember(Name = "demandRestored", Order = 18)] public bool DemandRestored { get; set; }
    [DataMember(Name = "state", Order = 19)] public PassengerContractState State { get; set; }
    [DataMember(Name = "resultCode", Order = 20)] public string ResultCode { get; set; } = "";
    [DataMember(Name = "version", Order = 21)] public long Version { get; set; }
}

public sealed class PassengerEconomyEngine
{
    private readonly object gate = new object();
    private readonly VehicleAcquisitionSnapshot state;
    private readonly INetworkRoleDetector authority;
    private readonly IMissionCompletionPort completion;

    public PassengerEconomyEngine(VehicleAcquisitionSnapshot state, INetworkRoleDetector authority, IMissionCompletionPort completion)
    { this.state = state ?? throw new ArgumentNullException(nameof(state)); this.authority = authority ?? throw new ArgumentNullException(nameof(authority)); this.completion = completion ?? throw new ArgumentNullException(nameof(completion)); VehicleAcquisitionPersistence.Validate(state); }

    public PassengerRouteDemand ConfigureRoute(string commandId, string routeId, string origin, string destination, int initialDemand, int maximumDemand,
        int demandPerInterval, long desiredFrequency, long fare, long latePenalty)
    {
        lock (gate)
        {
            RequireHost(); var fingerprint = string.Join("|", "route", routeId, origin, destination, initialDemand, maximumDemand, demandPerInterval, desiredFrequency, fare, latePenalty);
            var replay = Command(commandId, fingerprint); if (replay != null) return state.PassengerRoutes.Single(x => x.RouteId == replay.AssignmentId);
            if (string.IsNullOrWhiteSpace(routeId) || string.IsNullOrWhiteSpace(origin) || string.IsNullOrWhiteSpace(destination) || origin == destination || initialDemand < 0 || maximumDemand <= 0 || initialDemand > maximumDemand || demandPerInterval < 0 || desiredFrequency <= 0 || fare < 0 || latePenalty < 0) throw new ArgumentException("Invalid passenger route demand.");
            if (state.PassengerRoutes.Any(x => x.RouteId == routeId)) throw new InvalidOperationException("Passenger route identity already exists.");
            var route = new PassengerRouteDemand { RouteId = routeId, OriginId = origin.Trim(), DestinationId = destination.Trim(), DemandUnits = initialDemand, MaximumDemandUnits = maximumDemand, DemandPerInterval = demandPerInterval, DesiredFrequencyTicks = desiredFrequency, BaseFarePerPassenger = fare, LatePenaltyPerTick = latePenalty, DemandUpdatedTick = state.LeaseClock.ActiveTick, Version = 1 };
            state.PassengerRoutes.Add(route); Record(commandId, fingerprint, routeId, "passenger-route-configured"); return route;
        }
    }

    public PassengerRouteDemand RefreshDemand(string commandId, string routeId, long currentTick)
    {
        lock (gate)
        {
            RequireHost(); var fingerprint = "demand|" + routeId + "|" + currentTick; var replay = Command(commandId, fingerprint); if (replay != null) return state.PassengerRoutes.Single(x => x.RouteId == replay.AssignmentId);
            var route = state.PassengerRoutes.Single(x => x.RouteId == routeId); if (currentTick < route.DemandUpdatedTick) throw new InvalidOperationException("Passenger demand clock cannot move backwards.");
            var intervals = (currentTick - route.DemandUpdatedTick) / route.DesiredFrequencyTicks;
            if (intervals > 0) { var growth = checked(intervals * route.DemandPerInterval); route.DemandUnits = (int)Math.Min(route.MaximumDemandUnits, route.DemandUnits + growth); route.DemandUpdatedTick = checked(route.DemandUpdatedTick + intervals * route.DesiredFrequencyTicks); route.Version++; }
            Record(commandId, fingerprint, routeId, "passenger-demand-refreshed"); return route;
        }
    }

    public PassengerServiceContract OfferAndReserve(string commandId, string requesterId, string contractId, string routeId, string passengerJobId,
        IReadOnlyList<string> assetIds, AssetOwnerRef operatorRef, int capacity, long departureTick, long arrivalTick)
    {
        lock (gate)
        {
            RequireHost(); var fingerprint = string.Join("|", requesterId, contractId, routeId, passengerJobId, string.Join(",", assetIds ?? Array.Empty<string>()), operatorRef?.Key, capacity, departureTick, arrivalTick);
            var replay = Command(commandId, fingerprint); if (replay != null) return state.PassengerContracts.Single(x => x.ContractId == replay.AssignmentId);
            if (string.IsNullOrWhiteSpace(contractId) || string.IsNullOrWhiteSpace(passengerJobId) || assetIds == null || assetIds.Count == 0 || operatorRef == null || capacity <= 0 || departureTick < state.LeaseClock.ActiveTick || arrivalTick <= departureTick) throw new ArgumentException("Invalid passenger service offer.");
            if (state.PassengerContracts.Any(x => x.ContractId == contractId)) throw new InvalidOperationException("Passenger contract identity already exists.");
            var route = state.PassengerRoutes.Single(x => x.RouteId == routeId); var ids = assetIds.Distinct(StringComparer.Ordinal).OrderBy(x => x, StringComparer.Ordinal).ToList();
            if (!ids.Any(id => state.Fleet.Single(x => x.AssetId == id).Kind == FleetVehicleKind.PassengerCar)) throw new InvalidOperationException("A passenger service requires at least one registered passenger car.");
            var booked = Math.Min(capacity, route.DemandUnits); if (booked <= 0) throw new InvalidOperationException("Passenger demand is currently empty.");
            var maximum = checked(booked * route.BaseFarePerPassenger); var assignmentId = "passenger-assignment:" + contractId;
            new MissionAssignmentEngine(state, authority, completion).Reserve(commandId + ":assignment", requesterId, assignmentId, passengerJobId, MissionAssignmentKind.Passenger, ids, operatorRef, maximum);
            route.DemandUnits -= booked; route.Version++;
            var contract = new PassengerServiceContract { ContractId = contractId, RouteId = routeId, PassengerJobId = passengerJobId, AssignmentId = assignmentId, AssetIds = ids, Operator = Clone(operatorRef), Capacity = capacity, BookedPassengers = booked, ScheduledDepartureTick = departureTick, ScheduledArrivalTick = arrivalTick, MaximumQuotedRevenue = maximum, State = PassengerContractState.Reserved, ResultCode = "passenger-service-reserved", Version = 1 };
            state.PassengerContracts.Add(contract); Record(commandId, fingerprint, contractId, contract.ResultCode); return contract;
        }
    }

    public PassengerServiceContract Start(string commandId, string requesterId, string contractId, long vanillaBalance, long actualDepartureTick)
    {
        lock (gate)
        {
            RequireHost(); var fingerprint = string.Join("|", requesterId, contractId, vanillaBalance, actualDepartureTick, "start"); var replay = Command(commandId, fingerprint); if (replay != null) return state.PassengerContracts.Single(x => x.ContractId == replay.AssignmentId);
            var contract = state.PassengerContracts.Single(x => x.ContractId == contractId); if (contract.State != PassengerContractState.Reserved || actualDepartureTick < 0) throw new InvalidOperationException("Passenger service is not startable.");
            new MissionAssignmentEngine(state, authority, completion).Start(commandId + ":assignment", requesterId, contract.AssignmentId, vanillaBalance);
            contract.ActualDepartureTick = actualDepartureTick; contract.State = PassengerContractState.Active; contract.ResultCode = "passenger-service-active"; contract.Version++; Record(commandId, fingerprint, contractId, contract.ResultCode); return contract;
        }
    }

    public PassengerServiceContract Complete(string commandId, string requesterId, string contractId, long vanillaBalance, long actualArrivalTick, IReadOnlyList<string> arrivedAssetIds)
    {
        lock (gate)
        {
            RequireHost(); var arrived = arrivedAssetIds ?? Array.Empty<string>(); var fingerprint = string.Join("|", requesterId, contractId, vanillaBalance, actualArrivalTick, string.Join(",", arrived), "complete"); var replay = Command(commandId, fingerprint); if (replay != null) return state.PassengerContracts.Single(x => x.ContractId == replay.AssignmentId);
            var contract = state.PassengerContracts.Single(x => x.ContractId == contractId); if (contract.State != PassengerContractState.Active && contract.State != PassengerContractState.CompletionPending) throw new InvalidOperationException("Passenger service is not active.");
            var assignment = new MissionAssignmentEngine(state, authority, completion).Complete(commandId + ":assignment", requesterId, contract.AssignmentId, vanillaBalance, arrived);
            if (assignment.State == MissionAssignmentState.CompletionPending) { contract.State = PassengerContractState.CompletionPending; contract.ResultCode = assignment.ResultCode; contract.Version++; Record(commandId, fingerprint, contractId, contract.ResultCode); return contract; }
            contract.ActualArrivalTick = actualArrivalTick; ApplyFinancialAdjustment(contract, assignment); UpdateRoute(contract);
            contract.State = PassengerContractState.Completed; contract.ResultCode = "passenger-service-completed"; contract.Version++; Record(commandId, fingerprint, contractId, contract.ResultCode); return contract;
        }
    }

    public PassengerServiceContract Cancel(string commandId, string requesterId, string contractId)
    {
        lock (gate)
        {
            RequireHost(); var fingerprint = requesterId + "|" + contractId + "|cancel"; var replay = Command(commandId, fingerprint); if (replay != null) return state.PassengerContracts.Single(x => x.ContractId == replay.AssignmentId);
            var contract = state.PassengerContracts.Single(x => x.ContractId == contractId); if (contract.State == PassengerContractState.Completed || contract.State == PassengerContractState.Cancelled) throw new InvalidOperationException("Passenger contract is terminal.");
            new MissionAssignmentEngine(state, authority, completion).Cancel(commandId + ":assignment", requesterId, contract.AssignmentId);
            if (!contract.DemandRestored) { var route = state.PassengerRoutes.Single(x => x.RouteId == contract.RouteId); route.DemandUnits = Math.Min(route.MaximumDemandUnits, route.DemandUnits + contract.BookedPassengers); route.Version++; contract.DemandRestored = true; }
            contract.State = PassengerContractState.Cancelled; contract.ResultCode = "passenger-service-cancelled"; contract.Version++; Record(commandId, fingerprint, contractId, contract.ResultCode); return contract;
        }
    }

    private void ApplyFinancialAdjustment(PassengerServiceContract contract, MissionAssignment assignment)
    {
        if (contract.FinancialAdjustmentApplied) return;
        var observed = assignment.ActualRevenue; var lateTicks = Math.Max(0, contract.ActualArrivalTick!.Value - contract.ScheduledArrivalTick); var penalty = Math.Min(observed, checked(lateTicks * state.PassengerRoutes.Single(x => x.RouteId == contract.RouteId).LatePenaltyPerTick)); var paid = observed - penalty;
        var ledger = state.Economy.Ledger.SingleOrDefault(x => x.EntryId == assignment.AssignmentId + ":mission-revenue"); if (ledger != null) ledger.Amount = paid;
        if (assignment.Operator.Kind == AssetOwnerKind.Player)
        {
            var wallet = state.Economy.Wallets.Single(x => x.Account.Key == "Player:" + assignment.Operator.OwnerId); var expected = checked(assignment.VanillaBalanceBefore + paid); wallet.Balance = expected; wallet.Version++; assignment.ExpectedVanillaBalance = expected; assignment.ExternalSettlement = expected == assignment.VanillaBalanceAfter ? ExternalSettlementState.NotRequired : ExternalSettlementState.Pending;
        }
        else
        {
            var wallet = state.Economy.Wallets.Single(x => x.Account.Key == "Company:" + assignment.Operator.OwnerId); if (penalty > 0) { wallet.Balance -= penalty; wallet.Version++; }
        }
        assignment.ActualRevenue = paid; assignment.Version++; contract.ObservedVanillaRevenue = observed; contract.PunctualityPenalty = penalty; contract.PaidRevenue = paid; contract.FinancialAdjustmentApplied = true;
    }

    private void UpdateRoute(PassengerServiceContract contract)
    {
        var route = state.PassengerRoutes.Single(x => x.RouteId == contract.RouteId); var late = Math.Max(0, contract.ActualArrivalTick!.Value - contract.ScheduledArrivalTick); var score = (int)Math.Max(0, 10000 - Math.Min(10000, late * 10000 / route.DesiredFrequencyTicks));
        route.PunctualityBasisPoints = (int)(((long)route.PunctualityBasisPoints * route.CompletedServices + score) / (route.CompletedServices + 1)); route.CompletedServices++; route.TransportedPassengers = checked(route.TransportedPassengers + contract.BookedPassengers); route.LastCompletedServiceTick = contract.ActualArrivalTick; route.Version++;
    }

    private MissionAssignmentCommand? Command(string id, string fingerprint) { var r = state.PassengerCommands.SingleOrDefault(x => x.CommandId == id); if (r != null && r.Fingerprint != fingerprint) throw new InvalidOperationException("Passenger command ID payload conflict."); return r; }
    private void Record(string id, string fingerprint, string contract, string result) => state.PassengerCommands.Add(new MissionAssignmentCommand { CommandId = id, Fingerprint = fingerprint, AssignmentId = contract, ResultCode = result });
    private void RequireHost() { if (!NetworkAuthorityPolicy.CanExecuteEconomy(authority.Detect(), out var reason)) throw new InvalidOperationException(reason); }
    private static AssetOwnerRef Clone(AssetOwnerRef x) => new AssetOwnerRef { Kind = x.Kind, OwnerId = x.OwnerId };
}

public static class PassengerEconomyValidation
{
    public static void Validate(VehicleAcquisitionSnapshot state)
    {
        if (state.PassengerRoutes.GroupBy(x => x.RouteId).Any(x => x.Count() != 1) || state.PassengerContracts.GroupBy(x => x.ContractId).Any(x => x.Count() != 1) || state.PassengerCommands.GroupBy(x => x.CommandId).Any(x => x.Count() != 1)) throw new InvalidOperationException("Duplicate passenger economy identity.");
        foreach (var r in state.PassengerRoutes) if (string.IsNullOrWhiteSpace(r.RouteId) || string.IsNullOrWhiteSpace(r.OriginId) || string.IsNullOrWhiteSpace(r.DestinationId) || r.OriginId == r.DestinationId || r.DemandUnits < 0 || r.MaximumDemandUnits <= 0 || r.DemandUnits > r.MaximumDemandUnits || r.DemandPerInterval < 0 || r.DesiredFrequencyTicks <= 0 || r.BaseFarePerPassenger < 0 || r.LatePenaltyPerTick < 0 || r.PunctualityBasisPoints < 0 || r.PunctualityBasisPoints > 10000) throw new InvalidOperationException("Invalid passenger route demand.");
        foreach (var c in state.PassengerContracts) if (string.IsNullOrWhiteSpace(c.ContractId) || string.IsNullOrWhiteSpace(c.RouteId) || !state.PassengerRoutes.Any(r => r.RouteId == c.RouteId) || string.IsNullOrWhiteSpace(c.PassengerJobId) || string.IsNullOrWhiteSpace(c.AssignmentId) || !state.Assignments.Any(a => a.AssignmentId == c.AssignmentId) || c.AssetIds.Count == 0 || c.Capacity <= 0 || c.BookedPassengers <= 0 || c.BookedPassengers > c.Capacity || c.ScheduledArrivalTick <= c.ScheduledDepartureTick || c.MaximumQuotedRevenue < 0 || c.ObservedVanillaRevenue < 0 || c.PunctualityPenalty < 0 || c.PaidRevenue < 0) throw new InvalidOperationException("Invalid passenger service contract.");
    }
}
