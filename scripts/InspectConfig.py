"""Static path inspection only. Never execute a config or read its credentials."""
import ast
import json
import os
import pathlib
import sys


def inspect(config, directory):
    names = {"TARGET_STEAM_ID", "ST_LOGFILE", "CSV_FILE", "PROFILE_CSV_FILE",
             "STEAM_STATUS_FILE", "FILE_SUFFIX", "DISABLE_LOGGING", "GAMES_LIBRARY_CHECK"}
    values = {"ST_LOGFILE": "steam_monitor", "CSV_FILE": "", "PROFILE_CSV_FILE": "",
              "STEAM_STATUS_FILE": "", "FILE_SUFFIX": "", "DISABLE_LOGGING": False, "GAMES_LIBRARY_CHECK": False}
    tree = ast.parse(pathlib.Path(config).read_text(encoding="utf-8-sig"))
    for node in tree.body:
        if not isinstance(node, ast.Assign):
            raise ValueError("仅支持静态赋值配置；不执行配置中的代码。")
        for target in node.targets:
            if isinstance(target, ast.Name) and target.id in names:
                values[target.id] = ast.literal_eval(node.value)
    target = values.get("TARGET_STEAM_ID")
    if not isinstance(target, str) or not target.strip():
        raise ValueError("请先在所选配置中保存一个默认监测目标。")
    # A vanity/URL resolves to a different Steam64 filename at runtime. Explicit destinations
    # and FILE_SUFFIX are required here so collision detection never guesses that resolution.
    numeric = target.isdecimal()
    suffix = values["FILE_SUFFIX"] or (target if numeric else "")
    if not suffix:
        raise ValueError("非数字目标请在配置中指定 FILE_SUFFIX，确保输出检查准确。")
    def resolve(value):
        if not isinstance(value, str):
            raise ValueError("输出路径必须是静态字符串。")
        value = os.path.expanduser(value)
        if not os.path.isabs(value):
            value = os.path.join(directory, value)
        return os.path.normcase(os.path.realpath(value))
    outputs = []
    log_directory = directory
    if not values["DISABLE_LOGGING"]:
        base = values["ST_LOGFILE"]
        if not isinstance(base, str) or not base:
            raise ValueError("日志基础路径无效。")
        log = resolve(base if pathlib.Path(base).suffix else base + "_" + str(suffix) + ".log")
        outputs.append(log)
        log_directory = os.path.dirname(log)
    for key in ("CSV_FILE", "PROFILE_CSV_FILE"):
        if values[key]:
            outputs.append(resolve(values[key]))
    state = values["STEAM_STATUS_FILE"]
    if not state:
        if not numeric:
            raise ValueError("非数字目标请明确设置 STEAM_STATUS_FILE。")
        state = "steam_" + target + "_last_status.json"
    outputs.append(resolve(state))
    if values["GAMES_LIBRARY_CHECK"]:
        if not numeric:
            raise ValueError("启用游戏库快照时，请先保存数字目标以检查输出冲突。")
        outputs.append(resolve("steam_" + target + "_games.json"))
    if len(set(outputs)) != len(outputs):
        raise ValueError("该配置的多个输出指向同一文件。")
    return {"valid": True, "outputs": outputs, "logDirectory": log_directory, "error": ""}


if __name__ == "__main__":
    try:
        result = inspect(sys.argv[1], sys.argv[2])
    except (OSError, SyntaxError, ValueError, TypeError):
        result = {"valid": False, "outputs": [], "logDirectory": "",
                  "error": "无法安全确认默认目标和输出路径；请使用静态赋值并为每个实例设置独立输出。"}
    sys.stdout.reconfigure(encoding="utf-8")
    print(json.dumps(result, ensure_ascii=False))
