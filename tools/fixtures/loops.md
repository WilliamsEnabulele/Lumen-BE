# Loops

A loop is a promise that a block of code will run more than once, and a promise about
when it will stop. Everything hard about loops is the second half of that sentence.

## The counted loop

A counted loop runs a fixed number of times. You set a counter, you say how far it
goes, and you say how it moves. The canonical shape in most languages is three parts
separated by semicolons: start at zero, keep going while the counter is below the
limit, add one each time round.

The number of times the body runs is the limit minus the start. Starting at zero and
stopping below five runs the body five times, with the counter holding zero, one, two,
three and four. It never holds five inside the body — five is the value that ends it.
This is the single most common place beginners lose a run, and it is worth saying out
loud: the stopping value is the first value that is not used.

## The conditional loop

A conditional loop does not know how many times it will run. It runs while something
is true — while there are lines left in the file, while the user has not typed quit,
while the estimate is still moving. The counter, if there is one, is yours to manage.

This is where loops fail to stop. If nothing inside the body can ever make the
condition false, the loop runs forever. Reading a file without advancing to the next
line, or waiting for a flag that nothing sets, both produce a program that looks busy
and does nothing.

## Off-by-one

An off-by-one error is a loop that runs once too many times or once too few. It comes
from confusing the count of things with the highest index of those things. Five items
have indices zero through four, so the highest index is always one less than the count.
Using the count as an index reaches past the end of the collection.

## Breaking early

Sometimes the answer is found before the loop is done. Breaking out ends the loop
immediately, skipping the rest of the body and every remaining pass. It is the right
tool for a search: once you have found the thing, there is nothing left to look for.
The alternative — setting a flag and letting the loop run to its end — does the same
work but keeps going after the answer is known.
