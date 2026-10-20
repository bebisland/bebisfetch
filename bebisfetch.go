package main

import (
	"fmt"
	"os"
	"path/filepath"
	"strings"
	"syscall"
	"unicode/utf8"

	"github.com/shirou/gopsutil/v3/disk"
	"github.com/shirou/gopsutil/v3/host"
	"github.com/shirou/gopsutil/v3/mem"
)

var logo = []string{
	"      ▒███████     ▒███████      ",
	"   ▒██████████████████████████   ",
	"   ▒████░ ████  ████░ ███  ███   ",
	"   ▒████░       ████░      ███   ",
	"   ▒████░       ████░      ███   ",
	"   ▒████░       ████░      ███   ",
	"    ▒█████░    ██████░    ███▓▓▓▓",
	"▒▓▓▓▓▓▓██████████▓▓████████▓▓▓▓▓ ",
	"  ▒▓▓▓▓▓▓▓▓▓▓▓▓▓ ██ ▓▓▓▓▓▓▓▓▓▓▓  ",
	"    ▒▓▓▓▓▓▓▓▓▓▓      ▓▓▓▓▓▓▓▓▓▓  ",
	"    ▒▓▓▓▓▓▓▓▓▓▓      ▓▓▓▓▓▓▓▓▓▓  ",
	"      ▒▓▓▓▓▓▓▓▓▓    ▓▓▓▓▓▓▓▓▓    ",
	"         ▒▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓      ",
	"           ▒▓▓▓       ▒▓▓▓       ",
}

func color(code int, s any) string { return fmt.Sprintf("\x1b[%dm%v\x1b[0m", code, s) }

func bar(used, total uint64) string {

	f, col := int(float64(used)/float64(total)*10), 32

	if f > 8 {
		col = 31
	} else if f > 6 {
		col = 33
	}

	gib := func(b uint64) string { return color(97, fmt.Sprintf("%.1f", float64(b)/(1<<30))) }

	return color(col, strings.Repeat("■", f)) + color(90, strings.Repeat("■", 10-f)) +
		" " + gib(used) + color(90, " / ") + gib(total) + color(90, " GiB")
}

// Shortens a path
func short(path string) string {

	parts := strings.Split(path, "/")

	for i := 1; i < len(parts)-1; i++ {
		if r := []rune(parts[i]); len(r) > 0 {
			parts[i] = string(r[0])
		}
	}

	return strings.Join(parts, "/")
}

// Arranges rows into aligned strings
func align(rows [][3]string) []string {

	w := 0

	for _, r := range rows {
		w = max(w, utf8.RuneCountInString(r[0]+r[1]))
	}

	out := make([]string, len(rows))

	for i, r := range rows {
		pad := strings.Repeat(" ", w-utf8.RuneCountInString(r[0]+r[1]))
		out[i] = color(33, r[0]) + color(90, r[1]) + pad + " " + color(90, ":") + " " + r[2]
	}

	return out
}

func main() {

	// Fetch system information
	h, _ := host.Info()
	m, _ := mem.VirtualMemory()
	up := h.Uptime

	var pkgParts []string

	if pacman, err := os.ReadDir("/var/lib/pacman/local"); err == nil {

		if count := len(pacman); count > 0 {
			pkgParts = append(pkgParts, color(97, fmt.Sprint(count))+color(36, " pacman"))
		}
	}

	flatpak := 0
	for _, dir := range []string{"/var/lib/flatpak/app", "/var/lib/flatpak/runtime"} {
		entries, _ := os.ReadDir(dir)
		flatpak += len(entries)
	}
	if flatpak > 0 {
		pkgParts = append(pkgParts, color(97, fmt.Sprint(flatpak))+color(35, " flatpak"))
	}

	info := [][3]string{
		{"", "", color(34, os.Getenv("USER")) + color(90, "@") + color(36, h.Hostname)},
		{"󰏓", "", color(97, h.Platform)},
		{"", "", color(34, "Linux ") + color(33, h.KernelVersion)},
		{"󰥔", "", color(97, fmt.Sprintf("%dd %dh %dm", up/86400, up%86400/3600, up%3600/60))},
		{"", "", color(32, filepath.Base(os.Getenv("SHELL")))},
		{"󰿯", "", bar(m.Used, m.Total)},
	}

	if len(pkgParts) > 0 {
		combined := pkgParts[0]
		for _, p := range pkgParts[1:] {
			combined += color(90, ", ") + p
		}
		info = append(info, [3]string{"󰏖", "", combined})
	}

	// List disks, skip loop devices and /boot mountpoint
	var disks [][3]string

	parts, _ := disk.Partitions(false)
	seen := map[string]bool{}

	for _, p := range parts {

		if strings.HasPrefix(p.Device, "/dev/loop") || strings.HasPrefix(p.Mountpoint, "/boot") || seen[p.Device] {
			continue
		}

		seen[p.Device] = true

		var s syscall.Statfs_t

		if syscall.Statfs(p.Mountpoint, &s) == nil && s.Blocks > 0 {

			bs := uint64(s.Bsize)

			disks = append(disks, [3]string{"󱛟", " (" + short(p.Mountpoint) + ")", bar((s.Blocks-s.Bavail)*bs, s.Blocks*bs)})
		}
	}

	// Arrange logo and info side by side
	lines := append(align(info), align(disks)...)
	paint := strings.NewReplacer("█", color(97, "█"), "▓", color(33, "▓"), "▒", color(90, "▒"))

	for i := range max(len(logo), len(lines)) {

		var l, r string

		if i < len(logo) {
			l = logo[i]
		}
		if i < len(lines) {
			r = lines[i]
		}

		fmt.Println(paint.Replace(fmt.Sprintf("%-36s", l)) + r)
	}
}
