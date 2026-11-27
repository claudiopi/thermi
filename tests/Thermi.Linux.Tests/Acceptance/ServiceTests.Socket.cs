namespace Thermi.Linux.Tests.Acceptance;

/// <summary>The clients A3 runs in its container: Python's on the socket, and the shell each row runs as its users.</summary>
public sealed partial class ServiceTests
{
    // Opens as many connections to the socket as asked, says how many were served a line, and holds them until its input ends.
    private const string Clients = """
        import socket, sys
        held = [socket.socket(socket.AF_UNIX) for _ in range(int(sys.argv[1]))]
        for client in held: client.connect('/run/thermi/sensors.sock')
        print(sum(1 for client in held if client.makefile().readline()), flush=True)
        sys.stdin.read()
        """;

    // Writes to the socket, reads three lines, and says how much of what it wrote the service still left unread: a stream
    // socket's bytes count against their writer until the reader takes them.
    private const string Writer = """
        import fcntl, socket, struct, termios
        client = socket.socket(socket.AF_UNIX)
        client.connect('/run/thermi/sensors.sock')
        client.sendall(b'thermi\n' * 1024)
        lines = client.makefile()
        for _ in range(3): print(lines.readline(), end='', flush=True)
        print('unread', struct.unpack('i', fcntl.ioctl(client, termios.TIOCOUTQ, b'\0' * 4))[0], flush=True)
        """;

    // A server of another user's on a socket's path, which serves the recorded lines to whoever connects, as anyone may;
    // it says it listens on its first line.
    private const string Impostor = """
        import os, socket, sys
        server = socket.socket(socket.AF_UNIX)
        server.bind(sys.argv[1])
        os.chmod(sys.argv[1], 0o666)
        server.listen()
        print('listening', flush=True)
        lines = open(sys.argv[2], 'rb').read()
        while True:
            client, _ = server.accept()
            try:
                client.sendall(lines)
            except OSError:
                pass  # A client that refuses hangs up before the lines are all sent.
            client.close()
        """;

    // What status says of the service, and its problems' codes, on one line.
    private const string Codes = """
        import json, sys
        status = json.load(sys.stdin)
        print(status['service'], *sorted(problem['code'] for problem in status['problems']))
        """;

    // The CPU of the tree the person's console serves, which the service's own readings never name.
    private const string ConsoleCpu = "Console CPU";

    // Each row connects to the socket as it says: a person whose own console serves too; servers another user listens with
    // on the service's path and the person's own, which the person refuses, and the person's own console, which they read;
    // a client that writes as it reads; and nine connections of one user, then one of another's. The person runs the
    // thermi-cli the package put on every PATH.
    private static string ClientScript(Client client) => client switch
    {
        Client.Person => PersonScript(),
        Client.Impostor => ImpostorScript(),
        Client.Writer => $"su person -c 'python3 {Container.Work}/writer.py'",
        Client.Crowd => $"mkfifo /tmp/go /tmp/said; su person -c 'python3 {Container.Work}/clients.py 9' < /tmp/go > /tmp/said & exec 3>/tmp/go; "
            + $"read -r person < /tmp/said; su other -c 'python3 {Container.Work}/clients.py 1' < /dev/null; echo $person; exec 3>&-; wait",
        _ => throw new ArgumentOutOfRangeException(nameof(client), client, "Unknown client."),
    };

    // The person's console serves the tree, whose CPU the service never names: their read takes the service's lines, and
    // with THERMI_SENSORS_SOCKET naming the console's socket, the console's. Each says which it read.
    private static string PersonScript()
    {
        static string Names(string variable)
        {
            return $"$(su person -c '{variable} thermi-cli sensors --schema 1' | grep -q '{ConsoleCpu}' && echo yes || echo no)";
        }

        return "install -d -m 0777 /tmp/run && mkfifo /tmp/console || exit 1; "
            + Served("person", $"/usr/lib/thermi/thermi-sensors --console --root {Container.Work}/tree", "/tmp/console")
            + "su person -c 'XDG_RUNTIME_DIR=/tmp/run thermi-cli read --schema 1'; read=$?; "
            + $"echo service {Names("XDG_RUNTIME_DIR=/tmp/run")}; echo console {Names("THERMI_SENSORS_SOCKET=/tmp/run/thermi/sensors.sock")}; "
            + "pkill -u person; (exit $read)";
    }

    // Starts a server as a user, in the background with no stream of docker exec's, and waits for its first line.
    private static string Served(string who, string command, string ready) =>
        $"su {who} -s /bin/sh -c 'XDG_RUNTIME_DIR=/tmp/run exec {command}' > {ready} 2>/dev/null < /dev/null & read -r _ < {ready}; ";

    // With the service stopped, another user serves the service's path, which the person's read refuses, then the person's
    // own, which their status refuses too; then the person's own console, which it reads. Each status says its line.
    private static string ImpostorScript()
    {
        var work = Container.Work;
        const string Person = "su person -c 'XDG_RUNTIME_DIR=/tmp/run thermi-cli";
        return "systemctl stop thermi-sensors.socket thermi-sensors.service && rm -rf /run/thermi && install -d -o other /run/thermi"
            + " && install -d -m 0777 /tmp/run /tmp/run/thermi && mkfifo /tmp/system /tmp/own /tmp/console || exit 1; "
            + Served("other", $"python3 {work}/impostor.py /run/thermi/sensors.sock {work}/lines", "/tmp/system")
            + $"{Person} read --schema 1'; read=$?; {Person} status --schema 1' | python3 {work}/codes.py; "
            + "pkill -u other; rm -f /run/thermi/sensors.sock; "
            + Served("other", $"python3 {work}/impostor.py /tmp/run/thermi/sensors.sock {work}/lines", "/tmp/own")
            + $"{Person} status --schema 1' | python3 {work}/codes.py; pkill -u other; rm -f /tmp/run/thermi/sensors.sock; "
            + Served("person", $"/usr/lib/thermi/thermi-sensors --console --root {work}/tree", "/tmp/console")
            + $"{Person} status --schema 1' | python3 {work}/codes.py; pkill -u person; (exit $read)";
    }
}
