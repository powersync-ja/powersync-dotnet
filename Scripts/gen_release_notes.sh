#!/usr/bin/env bash

get_changelog_items() {
    awk '/^## /{count++; next} count==1' "$1" | sed '1d;$d'
}

get_package_name() {
    head -n 1 "$1" | awk '{print $2}'
}

get_package_version() {
    grep "\\S" "$1" | sed -n '2p' | awk '{print $2}'
}

release_notes() {
    # Print package name and version
    package_name=$(get_package_name "$1")
    package_version=$(get_package_version "$1")
    printf "# %s v%s\n\n" "$package_name" "$package_version"

    # Print changelog items
    get_changelog_items "$1"
}

release_notes "$1"
