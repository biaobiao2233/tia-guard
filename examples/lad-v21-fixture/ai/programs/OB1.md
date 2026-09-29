# Main / OB1

Language: LAD. Network order follows canonical compile-unit order.

Network inventory: **extracted** — Direct compile units in canonical XML order; no logic inference.

## Network 1

Language: LAD

Logic: **supported** — Complete bounded contact/coil topology; derived assignments apply at this network's execution.

Title (zh\-CN): Forward interlock

Comment (zh\-CN): 正转支路，反转输出作为常闭互锁。

Reads:

- ReverseOut (negated)
- StartForward

Writes and derived assignments (at this network's execution):

- ForwardOut = StartForward AND NOT ReverseOut

assignment-at-network-execution; networks execute in ordinal order; not simultaneous equations or a safety proof.

## Network 2

Language: LAD

Logic: **supported** — Complete bounded contact/coil topology; derived assignments apply at this network's execution.

Title (zh\-CN): Reverse interlock

Comment (zh\-CN): 反转支路，正转输出作为常闭互锁。

Reads:

- ForwardOut (negated)
- StartReverse

Writes and derived assignments (at this network's execution):

- ReverseOut = StartReverse AND NOT ForwardOut

assignment-at-network-execution; networks execute in ordinal order; not simultaneous equations or a safety proof.

## Network 3

Language: LAD

Logic: **supported** — Complete bounded contact/coil topology; derived assignments apply at this network's execution.

Title (zh\-CN): Parallel branches

Comment (zh\-CN): 同一电源母线上的两个并行支路，用于验证分支图。

Reads:

- StartForward
- StartReverse

Writes and derived assignments (at this network's execution):

- BranchA = StartForward
- BranchB = StartReverse

assignment-at-network-execution; networks execute in ordinal order; not simultaneous equations or a safety proof.

## Detected relationships

Analysis: **complete**.

- ForwardOut / ReverseOut: mutual-output-inhibit; reciprocal mandatory negated reads in unique output assignments. Sequential scan, not a runtime or safety guarantee.

Runtime behavior and safety: **unknown**. Relationships describe source-level dependencies; no inference from names, comments or partially parsed LAD.
