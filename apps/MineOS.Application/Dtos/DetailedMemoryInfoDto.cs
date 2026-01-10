namespace MineOS.Application.Dtos;

public record DetailedMemoryInfoDto(
    long VirtualMemory,
    long ResidentMemory,
    long SharedMemory
);
