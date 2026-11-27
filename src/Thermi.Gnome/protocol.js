// The protocol thermi and its extension speak over the session's D-Bus: the overlay's name, which only thermi's overlay
// holds, and the interface the extension serves. Its version is metadata.json's thermi-protocol.
export const OVERLAY = 'io.github.claudiopi.thermi.Overlay';
export const PATH = '/io/github/claudiopi/thermi';
const SHELL = 'io.github.claudiopi.thermi.Shell';
export const XML = `<node><interface name="${SHELL}">
  <property name="Protocol" type="u" access="read"/>
  <method name="Show"><arg type="u" direction="in"/><arg type="ay" direction="in"/><arg type="i" direction="in"/>
    <arg type="i" direction="in"/><arg type="i" direction="in"/><arg type="d" direction="in"/><arg type="d" direction="in"/>
    <arg type="d" direction="in"/><arg type="a(dddd)" direction="in"/></method>
  <method name="Move"><arg type="u" direction="in"/><arg type="d" direction="in"/><arg type="d" direction="in"/></method>
  <method name="Hide"><arg type="u" direction="in"/></method>
  <method name="Menu"><arg type="u" direction="in"/><arg type="d" direction="in"/><arg type="d" direction="in"/>
    <arg type="a(issbbb)" direction="in"/></method>
  <method name="Monitors"><arg type="a(ddddddbbdddd)" direction="out"/></method>
  <method name="Watch"><arg type="b" direction="in"/></method>
  <signal name="Ready"><arg type="u"/></signal>
  <signal name="Pointer"><arg type="u"/><arg type="s"/><arg type="d"/><arg type="d"/><arg type="u"/><arg type="u"/>
    <arg type="u"/><arg type="d"/><arg type="d"/></signal>
  <signal name="Key"><arg type="u"/><arg type="s"/></signal>
  <signal name="MonitorsChanged"><arg type="a(ddddddbbdddd)"/></signal>
  <signal name="Chosen"><arg type="u"/><arg type="i"/></signal>
</interface></node>`;
